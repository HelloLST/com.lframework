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
        /// - MonoBehaviour -> AddComponent 到本 GameObject
        /// - 纯 C# (有静态 Instance 属性, 如 SingletonBase 模式) -> 访问 Instance 触发懒创建
        /// </summary>
        private object CreateInstance(Type type)
        {
            // MonoBehaviour: AddComponent
            if (typeof(MonoBehaviour).IsAssignableFrom(type))
            {
                var mb = gameObject.AddComponent(type);
                return mb;
            }

            // 纯 C#: 尝试访问静态 Instance 属性 (SingletonBase 模式)
            var instanceProp = type.GetProperty("Instance",
                BindingFlags.Public | BindingFlags.Static);
            if (instanceProp != null)
            {
                return instanceProp.GetValue(null);
            }

            // 回退: 反射创建
            try
            {
                return Activator.CreateInstance(type, true);
            }
            catch (Exception e)
            {
                LogManager.Error($"[ModuleManager] 无法创建 {type.FullName}:\n{e}");
                return null;
            }
        }
    }
}
