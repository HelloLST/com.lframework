using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LFramework
{
    public class UnitySingleton<T> : MonoBehaviour where T : MonoBehaviour
    {
        private static readonly object _singletonLock = new object();
        public static bool IsExisted { get; private set; } = false;
        private static T _instance;

        // 标识应用/Editor 正在退出，避免在销毁阶段懒加载产生新的 GameObject
        // 否则会出现 "Some objects were not cleaned up when closing the scene" 警告
        private static bool _isQuitting = false;
        public static bool IsQuitting => _isQuitting;

        /// <summary>
        /// S2 修复: 仅在 Application.quitting 时置 _isQuitting = true，
        /// 不再在 OnDestroy 中置位(避免运行期单例销毁后其他单例 Instance 返回 null 的自锁死)。
        ///
        /// 会话重置：进 Play 清空上个会话的 _instance / IsExisted / _isQuitting 并重绑退出事件
        /// （旧实现只清 _isQuitting，实例字段残留，Phase 7 完整性测试发现）。
        /// 注意：引擎不会为【开放泛型类型】调用 [RuntimeInitializeOnLoadMethod]
        /// （已实测 Unity 2022.3.62f3c1）。真正的引擎入口在非泛型 RuntimeSessionReset，
        /// 由静态构造函数把本重置注册进去；下面的特性仅作为静态审计器的重置标记保留。
        /// </summary>
        static UnitySingleton()
        {
            RuntimeSessionReset.Register(ResetSessionState);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSessionState()
        {
            _instance = null;
            IsExisted = false;
            _isQuitting = false;
            Application.quitting -= OnApplicationQuitting;
            Application.quitting += OnApplicationQuitting;
        }

        private static void OnApplicationQuitting()
        {
            _isQuitting = true;
        }

        /// <summary>
        /// S1 修复: 模板方法旗标 —— 当本实例是重复实例时为 true，
        /// 派生类 Awake 在 base.Awake() 之后检查此旗标，若为 true 则直接 return，跳过 Init()。
        /// </summary>
        protected bool _isDuplicate;

        public static T Instance
        {
            get
            {
                // 仅在"既无实例又处于退出阶段"时返回 null，避免懒加载创建残留 GameObject。
                // 若 _instance 还活着，就照常返回——它马上要被 Unity 自然销毁，但当前帧仍可使用。
                if (_instance == null)
                {
                    if (_isQuitting) return null;

                    lock (_singletonLock)
                    {
                        if (_instance == null)
                        {
                            _instance = FindOrCreateInstance();
                            IsExisted = true;
                        }
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// 仅获取"已存在"的实例，绝不创建（Phase 5 / 5.4）：供日志这类基础设施使用，
        /// 写日志不应在模块清单外催生管理器实例。Unity 假 null 视为不存在。
        /// </summary>
        public static bool TryGetExisting(out T existing)
        {
            existing = _instance;
            return existing != null;
        }

        private static T FindOrCreateInstance()
        {
            // 仅查找激活的对象，符合Unity单例通常的使用习惯
            // 使用 FindFirstObjectByType，替代已废弃的全局 FindObjectOfType
            var instance = FindFirstObjectByType<T>();

            if (instance == null)
            {
                try
                {
                    GameObject go = new GameObject($"{typeof(T).Name}Singleton");
                    instance = go.AddComponent<T>();
                    DontDestroyOnLoad(go);
                }
                catch (Exception ex)
                {
                    LogManager.Error($"Failed to create singleton instance of {typeof(T).Name}: {ex.Message}");
                }
            }
            else
            {
                // 可选：如果场景中已有该对象，确保它也是 DontDestroyOnLoad 的，
                // 防止因为手动拖拽导致的销毁问题。
                // 注意：这取决于你的具体需求，因为有些单例可能只想在当前场景存在。
                DontDestroyOnLoad(instance.gameObject);
            }

            return instance;
        }

        protected virtual void Awake()
        {
            lock (_singletonLock)
            {
                if (_instance == null)
                {
                    _instance = this as T;
                    IsExisted = true;
                    _isDuplicate = false;
                    // 场景对象在场景加载早期（isLoaded=false）调用 DontDestroyOnLoad 会触发引擎断言
                    // "m_GameObjects.find(gameObject.GetInstanceID()) == m_GameObjects.end()"，
                    // 因此延迟到场景加载完成后执行，避免误报
                    EnsureDontDestroyOnLoad();
                    // 进入新会话时重置退出标记（Editor 重新点 Play）
                    _isQuitting = false;
                }
                else if (_instance != this)
                {
                    _isDuplicate = true;
                    Destroy(gameObject);
                    LogManager.Info($"[UnitySingleton] Duplicate instance of {typeof(T).Name} detected. Destroying {gameObject.name}");
                    return;
                }
            }
        }

        private void EnsureDontDestroyOnLoad()
        {
            if (gameObject.scene.isLoaded)
            {
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                SceneManager.sceneLoaded += OnSceneLoadedForDDOL;
            }
        }

        private void OnSceneLoadedForDDOL(Scene scene, LoadSceneMode mode)
        {
            SceneManager.sceneLoaded -= OnSceneLoadedForDDOL;
            if (this == null) return;
            DontDestroyOnLoad(gameObject);
        }

        protected virtual void OnApplicationQuit()
        {
            _isQuitting = true;
        }

        protected virtual void OnDestroy()
        {
            lock (_singletonLock)
            {
                if (_instance == this)
                {
                    _instance = null;
                    IsExisted = false;
                    // S2 修复: 不再在此处置 _isQuitting = true
                    // 运行期单例销毁(如场景卸载)不应阻止其他单例的懒加载
                    // _isQuitting 仅由 Application.quitting 事件置位
                }
            }
        }
    }
}
