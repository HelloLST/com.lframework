using System;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace LFramework
{
    /// <summary>
    /// 模块管理器 - 框架启动中枢。
    /// 读取 ModuleManifest SO 清单 -> 拓扑排序 -> 有序创建模块实例 -> 依次初始化。
    /// 替代 GameLaunch 中硬编码的 AddComponent 链，使模块清单可由策划在 Inspector 中编辑。
    /// 逐帧派发：仅对实现 <see cref="ITickable"/> 的模块调用 Tick（Phase 5 / 5.1）。
    /// </summary>
    public class ModuleManager : MonoBehaviour
    {
        // ================================================================
        // 静态退出标记
        // ================================================================

        private static bool s_isQuitting;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetQuitFlag()
        {
            s_isQuitting = false;
            Application.quitting -= OnApplicationQuitting;
            Application.quitting += OnApplicationQuitting;
        }

        private static void OnApplicationQuitting()
        {
            s_isQuitting = true;
        }

        // ================================================================
        // 实例状态
        // ================================================================

        /// <summary>已创建并实现了 IModule 的模块列表(按初始化顺序)</summary>
        private readonly List<IModule> _modules = new List<IModule>();

        /// <summary>所有已创建的模块实例(MonoBehaviour 和纯 C# 都在里面),按 typeName 索引</summary>
        private readonly Dictionary<string, object> _instancesByTypeName = new Dictionary<string, object>();

        /// <summary>是否已完成初始化</summary>
        public bool IsInitialized { get; private set; }

        /// <summary>缓存的 MonoManager 引用，用于统一逐帧派发</summary>
        private MonoManager _monoManager;

        // ================================================================
        // 公共 API
        // ================================================================

        /// <summary>
        /// 读取清单并按拓扑排序创建/初始化所有模块。
        /// 对 MonoBehaviour 类型: AddComponent 到本 GameObject;
        /// 对纯 C# 类型(SingletonBase 模式): 访问静态 Instance 属性触发懒创建。
        /// </summary>
        public async UniTask InitializeAsync(ModuleManifest manifest)
        {
            if (manifest == null)
            {
                LogManager.Error("[ModuleManager] manifest 为 null，无法初始化模块。请在 GameLaunch Inspector 中赋值。");
                return;
            }

            var sortedEntries = manifest.GetSortedEntries();

            for (int i = 0; i < sortedEntries.Count; i++)
            {
                var entry = sortedEntries[i];

                Type type = ResolveType(entry.typeName);
                if (type == null)
                {
                    LogManager.Error($"[ModuleManager] 无法解析类型: {entry.typeName} (条目: \"{entry.displayName}\")，跳过。");
                    continue;
                }

                object instance = CreateInstance(type);
                if (instance == null)
                {
                    LogManager.Error($"[ModuleManager] 创建实例失败: {entry.typeName}，跳过。");
                    continue;
                }

                _instancesByTypeName[entry.typeName] = instance;

                if (instance is IModule module)
                {
                    _modules.Add(module);
                    try
                    {
                        await module.InitializeAsync();
                        LogManager.Info($"[ModuleManager] 模块已初始化 ({i + 1}/{sortedEntries.Count}): {module.DisplayName}");
                    }
                    catch (Exception e)
                    {
                        LogManager.Error($"[ModuleManager] 模块初始化异常: {entry.typeName}\n{e}");
                    }
                }
                else
                {
                    LogManager.Info($"[ModuleManager] 模块已创建 (非 IModule): {type.Name}");
                }
            }

            IsInitialized = true;

            // 统一逐帧派发：注册到 MonoManager 的 Update 监听
            _monoManager = GetModule<MonoManager>();
            if (_monoManager != null)
            {
                _monoManager.AddUpdateListener(OnModulesTick);
                LogManager.Info("[ModuleManager] Tick 派发已注册到 MonoManager.updateEvent。");
            }
            else
            {
                LogManager.Warning("[ModuleManager] 未找到 MonoManager，ITickable 将不会被调用。请确保清单中包含 MonoManager。");
            }

            LogManager.Info($"[ModuleManager] 全部模块初始化完成，共 {_modules.Count} 个 IModule + {_instancesByTypeName.Count - _modules.Count} 个非 IModule。");
        }

        /// <summary>按 typeName 获取已创建的模块实例</summary>
        public T GetModule<T>() where T : class
        {
            string typeName = typeof(T).FullName;
            if (typeName != null && _instancesByTypeName.TryGetValue(typeName, out var instance))
                return instance as T;
            return null;
        }

        // ================================================================
        // 逐帧 Tick — 由 MonoManager.Update 统一驱动，不再自带 Update()
        // ================================================================

        /// <summary>
        /// MonoManager Update 回调：按初始化顺序调用所有 ITickable.Tick。
        /// 全工程的逐帧逻辑统一从 MonoManager.Update() → 此方法流出，
        /// 避免多个 MonoBehaviour.Update() 各自为政。
        /// </summary>
        private void OnModulesTick()
        {
            if (!IsInitialized) return;
            float dt = Time.deltaTime;
            for (int i = 0; i < _modules.Count; i++)
            {
                if (_modules[i] is ITickable tickable)
                {
                    try { tickable.Tick(dt); }
                    catch (Exception e) { LogManager.Error($"[ModuleManager] Tick 异常 ({_modules[i].DisplayName}):\n{e}"); }
                }
            }
        }

        private void OnDestroy()
        {
            // 反注册逐帧回调（即使退出也尝试，MonoManager 可能还活着）
            if (_monoManager != null)
            {
                _monoManager.RemoveUpdateListener(OnModulesTick);
                _monoManager = null;
            }

            // 应用退出时由各管理器自身的 OnDestroy 处理清理，避免逆序 Dispose 撞上 Unity 随机销毁顺序
            if (s_isQuitting) return;

            // 运行期销毁: 逆序 Dispose
            for (int i = _modules.Count - 1; i >= 0; i--)
            {
                try { _modules[i].Dispose(); }
                catch (Exception e) { LogManager.Error($"[ModuleManager] Dispose 异常 ({_modules[i].DisplayName}):\n{e}"); }
            }
            _modules.Clear();
            _instancesByTypeName.Clear();
            IsInitialized = false;
        }

        // ================================================================
        // 内部: 单例探针发现（Phase 9：修复"幽灵实例"）
        // ================================================================

        /// <summary>
        /// 缓存 typeName -> "获取单例实例"的委托。每帧/每模块只反射一次。
        /// </summary>
        private static readonly Dictionary<string, Func<object>> _instanceAccessors =
            new Dictionary<string, Func<object>>();

        /// <summary>缓存 typeName -> <c>UnitySingleton&lt;T&gt;.TryGetExisting</c>，值可能为 null。</summary>
        private static readonly Dictionary<string, MethodInfo> _existingProbes =
            new Dictionary<string, MethodInfo>();

        /// <summary>
        /// 查找继承链上的 <c>static bool TryGetExisting(out T)</c>（<see cref="UnitySingleton{T}"/> 提供）。
        /// 用途：MonoBehaviour 模块在 AddComponent 之前先探一次"是不是已经有活着的单例"。
        /// 否则一旦 <c>UIManager.Instance</c> 之类在模块初始化之前被访问过，
        /// AddComponent 会造出第二个实例，它立刻在 <c>Awake</c> 里把自己 Destroy 掉，
        /// 而 <c>_instancesByTypeName</c> 记下的正是这个将死的对象 —— 与幽灵实例同一类病。
        /// </summary>
        private static MethodInfo FindExistingProbe(Type type)
        {
            string key = type.FullName ?? type.Name;
            if (_existingProbes.TryGetValue(key, out var cached)) return cached;

            MethodInfo probe = null;
            for (Type current = type; current != null && probe == null; current = current.BaseType)
            {
                foreach (var method in current.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    if (method.Name != "TryGetExisting") continue;
                    if (method.ReturnType != typeof(bool)) continue;
                    var parameters = method.GetParameters();
                    if (parameters.Length != 1 || !parameters[0].ParameterType.IsByRef) continue;
                    // out 参数的值类型必须能装下模块类型，避免误用无关的单例探针
                    if (!parameters[0].ParameterType.GetElementType().IsAssignableFrom(type)) continue;
                    probe = method;
                    break;
                }
            }

            _existingProbes[key] = probe;   // 缓存 null 同样有意义：避免每次重扫继承链
            return probe;
        }

        /// <summary>探测"已存在的单例实例"，没有则返回 null（绝不创建）。</summary>
        private static object ProbeExistingSingleton(Type type)
        {
            var probe = FindExistingProbe(type);
            if (probe == null) return null;

            try
            {
                var args = new object[] { null };
                if (!(bool)probe.Invoke(null, args)) return null;
                object existing = args[0];
                if (existing == null) return null;
                // Unity 假 null（已被销毁的组件）视为不存在
                if (existing is UnityEngine.Object unityObject && unityObject == null) return null;
                return existing;
            }
            catch (Exception e)
            {
                LogManager.Warning($"[ModuleManager] 探测 {type.FullName} 的既有单例失败，按新建处理：{e.Message}");
                return null;
            }
        }

        /// <summary>
        /// 在整条继承链上查找 public static 的 Instance 属性。
        ///
        /// 教训（Phase 9，测试抓到）：<c>type.GetProperty("Instance", Public|Static)</c> **不会**
        /// 返回声明在泛型基类上的静态属性 —— <c>ResourcesManager : SingletonBase&lt;ResourcesManager&gt;</c>
        /// 的 Instance 实际声明在 <c>SingletonBase&lt;T&gt;</c> 上，于是这里恒为 null，
        /// CreateInstance 一路掉进 Activator 回退分支，造出一个 Init() 从未执行的"幽灵实例"：
        /// 它与 <c>ResourcesManager.Instance</c> 不是同一个对象，<c>_loader</c> 为 null，
        /// 平时看不出来（业务都走 Instance），直到 Dispose 才炸 NullReferenceException。
        /// 因此必须逐级 BaseType 查找，且只认返回类型可赋值给模块类型的属性，
        /// 避免误把无关类型的 Instance 当成模块实例。
        /// </summary>
        private static PropertyInfo FindSingletonInstanceProperty(Type type)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                var prop = current.GetProperty(
                    "Instance",
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
                if (prop == null) continue;
                if (!type.IsAssignableFrom(prop.PropertyType)) continue;
                return prop;
            }
            return null;
        }

        /// <summary>
        /// 生成"获取该类型单例实例"的委托；没有可用的静态 Instance 时返回 null。
        /// </summary>
        private static Func<object> GetOrCreateInstanceAccessor(Type type)
        {
            string key = type.FullName ?? type.Name;
            if (_instanceAccessors.TryGetValue(key, out var cached)) return cached;

            Func<object> accessor = null;
            var prop = FindSingletonInstanceProperty(type);
            if (prop != null && prop.CanRead)
            {
                var getter = prop.GetGetMethod();
                if (getter != null)
                {
                    accessor = (Func<object>)Delegate.CreateDelegate(typeof(Func<object>), getter);
                }
            }

            _instanceAccessors[key] = accessor;   // 缓存 null 结果同样有意义：避免每次重扫继承链
            return accessor;
        }

        // ================================================================
        // 内部: 类型解析
        // ================================================================

        /// <summary>
        /// 跨程序集搜索类型。先试 Type.GetType(含程序集限定名)，
        /// 失败则遍历 AppDomain 所有程序集按 FullName 匹配。
        /// </summary>
        public static Type ResolveType(string typeName)   // public：供 ModuleManifestCreator 编辑期校验复用
        {
            if (string.IsNullOrEmpty(typeName)) return null;

            // 快速路径: Type.GetType 支持带程序集限定的全名
            Type type = Type.GetType(typeName);
            if (type != null) return type;

            // 慢速路径: 遍历所有程序集
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = asm.GetType(typeName);
                if (type != null) return type;
            }
            return null;
        }

        // ================================================================
        // 内部: 实例创建
        // ================================================================

        /// <summary>
        /// 根据类型创建模块实例:
        /// - MonoBehaviour -> 先探既有单例，没有再 AddComponent 到本 GameObject
        /// - 纯 C# (继承链上有静态 Instance, 如 SingletonBase 模式) -> 访问 Instance 触发懒创建 + Init
        /// - 其余 -> 反射无参构造（会跳过 Init，见下方告警）
        /// </summary>
        private object CreateInstance(Type type)
        {
            // MonoBehaviour: 先看是不是已经有活着的单例，避免造出"即将自我销毁的重复实例"
            if (typeof(MonoBehaviour).IsAssignableFrom(type))
            {
                // 如果 Instance 已被提前访问过，这里直接沿用，不再 AddComponent
                var existing = ProbeExistingSingleton(type);
                if (existing != null)
                {
                    LogManager.Info($"[ModuleManager] {type.FullName} 已有存活单例，直接沿用（不重复创建）。");
                    return existing;
                }

                var mb = gameObject.AddComponent(type);

                // AddComponent 期间 Awake 已执行；若我们刚创建的这个并不是最终单例，
                // 说明存在并发/提前访问，改用它注册的那个实例，别把将死的对象记进清单。
                var settled = ProbeExistingSingleton(type);
                if (settled != null && !ReferenceEquals(settled, mb)) return settled;

                return mb;
            }

            // 纯 C#: 走静态 Instance（SingletonBase / UnitySingleton 之外的懒加载单例模式）
            var accessor = GetOrCreateInstanceAccessor(type);
            if (accessor != null)
            {
                try
                {
                    object singleton = accessor();
                    if (singleton != null) return singleton;
                    LogManager.Error($"[ModuleManager] {type.FullName}.Instance 返回 null，改用反射创建（Init 不会执行）。");
                }
                catch (Exception e)
                {
                    LogManager.Error($"[ModuleManager] 访问 {type.FullName}.Instance 失败，改用反射创建（Init 不会执行）:\n{e}");
                }
            }

            // 回退: 反射创建。注意这条路径**不会**执行 SingletonBase.Init()，
            // 模块必须自己在 InitializeAsync 里完成初始化，否则会带着未初始化的字段运行。
            try
            {
                var created = Activator.CreateInstance(type, true);
                LogManager.Warning(
                    $"[ModuleManager] {type.FullName} 没有可用的静态 Instance，走反射创建：" +
                    "Init() 不会被执行，请把初始化逻辑放进 InitializeAsync()。");
                return created;
            }
            catch (Exception e)
            {
                LogManager.Error($"[ModuleManager] 无法创建 {type.FullName}:\n{e}");
                return null;
            }
        }
    }
}
