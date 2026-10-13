using UnityEngine;

namespace LFramework
{
    /// <summary>
    /// Resources 配置资产缓存（Phase 5 / 5.5）。
    /// 统一收敛三件原本各写一份的样板：静态缓存 + 懒加载 + 关闭 Domain Reload 时进 Play 清空。
    /// 泛型静态字段按 T 天然隔离；UISettings / LogSettings / ArchitectureGuardrailSettings 均已迁移。
    /// 用法：<code>ScriptableConfigCache&lt;UISettings&gt;.Get("UISettings")</code>
    /// </summary>
    public static class ScriptableConfigCache<T> where T : UnityEngine.Object
    {
        private static T _cached;
        private static bool _loaded;

        // 引擎不会为【开放泛型类型】调用 [RuntimeInitializeOnLoadMethod]（已实测 Unity 2022.3.62f3c1）。
        // 真正的引擎入口在非泛型 RuntimeSessionReset，由静态构造函数注册；
        // 下面的特性仅作为静态审计器的重置标记保留。
        static ScriptableConfigCache()
        {
            RuntimeSessionReset.Register(ResetForNewSession);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForNewSession()
        {
            _cached = null;
            _loaded = false;
        }

        /// <summary>取缓存资产；首次调用时从 Resources 加载。可能为 null，由调用方走默认值。</summary>
        public static T Get(string resourcesPath)
        {
            if (!_loaded)
            {
                _loaded = true;
                _cached = Resources.Load<T>(resourcesPath);
            }
            return _cached;
        }

        /// <summary>清除缓存，下次 Get 重新加载。</summary>
        public static void Invalidate()
        {
            _cached = null;
            _loaded = false;
        }
    }
}
