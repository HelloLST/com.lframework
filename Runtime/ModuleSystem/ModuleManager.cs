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

        /// <summary>
        /// 初始化过程中失败的模块（typeName → 异常）。
        ///
        /// 修复（打磨台账 C1）：此前单模块 <c>InitializeAsync</c> 抛异常只 log 后继续，
        /// 最终仍无条件 <c>IsInitialized = true</c>，业务等到了"初始化完成"却不知道
        /// 某个模块是坏的 —— 典型的"启动看着正常、用起来才炸"。
        /// 现在失败被显式收集，<see cref="IsInitialized"/> 表示"流程走完"而非"全部成功"，
        /// 调用方可据此决定是否中断启动。
        /// </summary>
        public IReadOnlyDictionary<string, Exception> FailedModules => _failedModules;
        private readonly Dictionary<string, Exception> _failedModules = new Dictionary<string, Exception>();

        /// <summary>
        /// 因依赖不可用而**被跳过**的模块（typeName → 原因）。
        ///
        /// 修复（打磨台账 C2）：此前 <c>dependsOn</c> 只参与拓扑排序，
        /// **不校验"依赖方是否真的初始化成功"**。于是 A 依赖 B、B 初始化失败时，
        /// A 照样初始化 —— 它拿到的 <c>GetModule&lt;B&gt;()</c> 是 null，
        /// 运行到第一次使用才炸，堆栈指向 A 的业务代码，看不出根因在 B。
        ///
        /// 现在依赖不满足的模块**根本不创建实例**（避免 MonoBehaviour 的 Awake 先跑一遍），
        /// 而是记进这里，并可在 <see cref="GetModule{T}"/> 时给出明确原因。
        /// </summary>
        public IReadOnlyDictionary<string, string> SkippedModules => _skippedModules;
        private readonly Dictionary<string, string> _skippedModules = new Dictionary<string, string>();

        /// <summary>
        /// 所有"已创建且（若实现 IModule）初始化成功"的 typeName。
        /// 依赖校验的唯一依据：<c>dependsOn</c> 里的每一项都必须在这里面。
        /// </summary>
        private readonly HashSet<string> _availableTypeNames = new HashSet<string>();

        /// <summary>是否存在初始化失败或被跳过的模块。</summary>
        public bool HasFailures => _failedModules.Count > 0 || _skippedModules.Count > 0;

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

                // 依赖校验必须在**创建实例之前**做。
                // MonoBehaviour 模块一旦 AddComponent，Awake 立刻执行并可能访问别的模块；
                // 等创建完再判断"要不要初始化"，那时副作用已经发生了。
                if (!TryValidateDependencies(entry)) continue;

                Type type = ResolveType(entry.typeName);
                if (type == null)
                {
                    LogManager.Error($"[ModuleManager] 无法解析类型: {entry.typeName} (条目: \"{entry.displayName}\")，跳过。");
                    _skippedModules[entry.typeName] = "类型无法解析";
                    continue;
                }

                object instance = CreateInstance(type);
                if (instance == null)
                {
                    LogManager.Error($"[ModuleManager] 创建实例失败: {entry.typeName}，跳过。");
                    _skippedModules[entry.typeName] = "实例创建失败";
                    continue;
                }

                _instancesByTypeName[entry.typeName] = instance;

                if (instance is IModule module)
                {
                    try
                    {
                        await module.InitializeAsync();
                        _modules.Add(module);
                        _availableTypeNames.Add(entry.typeName);
                        LogManager.Info($"[ModuleManager] 模块已初始化 ({i + 1}/{sortedEntries.Count}): {module.DisplayName}");
                    }
                    catch (Exception e)
                    {
                        // 修复（打磨台账 C1）：失败模块**不进 Tick 派发列表**，并记录到
                        // FailedModules 供调用方查询。此前它既被加进 _modules 参与每帧 Tick，
                        // 又让 IsInitialized 照样为 true，坏模块会以半初始化状态持续运行。
                        _failedModules[entry.typeName] = e;
                        LogManager.Error(
                            $"[ModuleManager] 模块初始化失败，已从 Tick 列表移除: {entry.typeName} ({module.DisplayName})\n" +
                            $"依赖它的模块将被跳过（见 SkippedModules）。\n{e}");
                    }
                }
                else
                {
                    // 非 IModule 没有初始化契约，创建成功即视为可用
                    _availableTypeNames.Add(entry.typeName);
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

            if (HasFailures)
            {
                // 分别报告"失败"与"被跳过"：两者处置方式不同 ——
                // 失败的需要看异常栈，被跳过的需要看依赖链。
                var parts = new List<string>();
                if (_failedModules.Count > 0)
                    parts.Add($"{_failedModules.Count} 个初始化失败：{string.Join(", ", _failedModules.Keys)}");
                if (_skippedModules.Count > 0)
                    parts.Add($"{_skippedModules.Count} 个因依赖不可用被跳过：{string.Join(", ", _skippedModules.Keys)}");

                LogManager.Error(
                    $"[ModuleManager] 初始化结束，但存在异常模块 —— {string.Join("；", parts)}。" +
                    "请检查 FailedModules / SkippedModules 或上方错误日志。");
            }
            else
            {
                LogManager.Info($"[ModuleManager] 全部模块初始化完成，共 {_modules.Count} 个 IModule + {_instancesByTypeName.Count - _modules.Count} 个非 IModule。");
            }

        }

        /// <summary>
        /// 校验条目的 <c>dependsOn</c> 是否全部可用（C2）。
        ///
        /// 判定依据是 <see cref="_availableTypeNames"/> —— 只有**真正初始化成功**的模块才在里面。
        /// 因此「依赖项不在清单里」「依赖项被禁用」「依赖项初始化抛异常」三种情况
        /// 都会导致本模块被跳过，而不再是一路绿灯往下跑。
        ///
        /// 之所以要**连带跳过间接依赖**：A→B→C，C 挂了，B 被跳过，
        /// 那么 A 的依赖也是坏的。因为按拓扑序处理，B 被跳过时不会进
        /// <see cref="_availableTypeNames"/>，A 的校验自然也会失败，无需额外传播逻辑。
        /// </summary>
        private bool TryValidateDependencies(ModuleEntry entry)
        {
            if (entry.dependsOn == null || entry.dependsOn.Length == 0) return true;

            string missing = null;
            for (int i = 0; i < entry.dependsOn.Length; i++)
            {
                string dep = entry.dependsOn[i];
                if (string.IsNullOrEmpty(dep)) continue;
                if (_availableTypeNames.Contains(dep)) continue;

                missing = dep;
                break;
            }

            if (missing == null) return true;

            string reason = BuildDependencyFailureReason(missing);
            _skippedModules[entry.typeName] = reason;

            LogManager.Error(
                $"[ModuleManager] 模块 \"{entry.displayName}\" ({entry.typeName}) 被跳过：{reason}\n" +
                $"跳过是安全的 —— 该模块的 InitializeAsync 不会执行，" +
                $"GetModule<{ShortName(entry.typeName)}>() 会返回 null 并给出这条原因。");

            return false;
        }

        /// <summary>把"依赖为什么不可用"翻译成一句能直接定位问题的话。</summary>
        private string BuildDependencyFailureReason(string dependencyTypeName)
        {
            if (_failedModules.TryGetValue(dependencyTypeName, out Exception depError))
            {
                return $"依赖的模块 {dependencyTypeName} 初始化失败（{depError.GetType().Name}: {depError.Message}）";
            }

            if (_skippedModules.TryGetValue(dependencyTypeName, out string transitive))
            {
                return $"依赖的模块 {dependencyTypeName} 自身也被跳过（{transitive}）";
            }

            if (_instancesByTypeName.ContainsKey(dependencyTypeName))
            {
                return $"依赖的模块 {dependencyTypeName} 已创建但尚未就绪";
            }

            return $"依赖的模块 {dependencyTypeName} 不在清单中或未启用";
        }

        private static string ShortName(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return "?";
            int dot = typeName.LastIndexOf('.');
            return dot >= 0 && dot < typeName.Length - 1 ? typeName.Substring(dot + 1) : typeName;
        }

        /// <summary>按 typeName 获取已创建的模块实例</summary>
        public T GetModule<T>() where T : class
        {
            string typeName = typeof(T).FullName;
            if (typeName != null && _instancesByTypeName.TryGetValue(typeName, out var instance))
                return instance as T;

            // 修复（打磨台账 C2）：模块因为依赖坏掉而被跳过时，这里必须说明原因。
            // 否则业务拿到一个裸 null，堆栈指向自己的代码，完全看不出根因在别的模块。
            if (typeName != null && _skippedModules.TryGetValue(typeName, out string skipReason))
            {
                LogManager.Warning(
                    $"[ModuleManager] GetModule<{typeof(T).Name}>() 返回 null —— 该模块因依赖不可用已被跳过：{skipReason}");
                return null;
            }

            // 修复（打磨台账 C3）：未初始化时返回 null 且完全无提示，业务在 Awake 里调用
            // 会得到 NullReferenceException，而堆栈指向业务代码 —— 看不出根因是"模块还没初始化"。
            // 这里只在**确实没初始化**时提示，避免"模块从未在清单中"这种正常情况刷屏。
            if (!IsInitialized)
            {
                LogManager.Warning(
                    $"[ModuleManager] GetModule<{typeof(T).Name}>() 在模块初始化完成前被调用，返回 null。" +
                    "如果你在 Awake 里取模块，请改到 InitializeAsync 之后（或等待 IsInitialized）。");
            }
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
            _failedModules.Clear();
            _skippedModules.Clear();
            _availableTypeNames.Clear();
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
