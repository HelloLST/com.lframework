using UnityEngine;

namespace LFramework.Architecture
{
    /// <summary>
    /// 暂停计数服务：用"申请/释放"计数替代各处裸 Time.timeScale 赋值。
    /// 多个系统可各自申请暂停（如打开帮助面板、打开弹窗），只有所有申请都释放后游戏才真正恢复，
    /// 避免"先关闭的某个面板把游戏提前恢复"这类靠巧合工作的 bug。
    ///
    /// 用法：
    /// - 需要暂停时调用 RequestPause()
    /// - 不再需要时调用 ReleasePause()
    /// 本服务直接管理 Time.timeScale，是唯一的写入出口。
    /// </summary>
    [CreateAssetMenu(menuName = "LFramework/Architecture/Game Pause Service")]
    public class GamePauseService : ScriptableObject
    {
        // 运行时计数不序列化：把暂停计数写进 SO 资产是历史 bug（"跑一遍游戏，资产被改"）。
        // [NonSerialized] 同时避免关闭 Domain Reload 时残留计数在 Inspector 里误导排查。
        [System.NonSerialized] private int _pauseRequestCount;

        /// <summary>当前是否处于暂停状态（有任意一方申请了暂停即为 true）。</summary>
        public bool IsPaused => _pauseRequestCount > 0;

        /// <summary>当前暂停申请计数，仅供调试/检视。</summary>
        public int PauseRequestCount => _pauseRequestCount;

        /// <summary>申请暂停（计数 +1）。</summary>
        public void RequestPause()
        {
            _pauseRequestCount++;
            Apply();
        }

        /// <summary>释放暂停（计数 -1，最低为 0）。</summary>
        public void ReleasePause()
        {
            _pauseRequestCount = Mathf.Max(0, _pauseRequestCount - 1);
            Apply();
        }

        /// <summary>强制清空所有暂停申请并恢复（极端兜底，如场景切换时）。</summary>
        public void ResetPause()
        {
            _pauseRequestCount = 0;
            Apply();
        }

        private void Apply()
        {
            // 唯一出口：只有所有申请都释放后才恢复 timeScale=1
            Time.timeScale = _pauseRequestCount > 0 ? 0f : 1f;
        }

        /// <summary>
        /// 关闭 Domain Reload 时 SO 实例的 OnEnable 不会重新执行，
        /// 上一次 Play 的暂停计数会带进下一次（第二次进 Play 直接处于暂停态）。
        /// 进入 Play 时强制清零并恢复时间缩放（Phase 1 / P0-2 修复）。
        /// Domain Reload 开启时静态本就全新，此方法幂等无害。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForNewSession()
        {
            Time.timeScale = 1f;
            var all = Resources.FindObjectsOfTypeAll<GamePauseService>();
            for (int i = 0; i < all.Length; i++)
                all[i]._pauseRequestCount = 0;
        }

        [ContextMenu("Reset to Defaults")]
        private void ResetToDefaults() => ResetPause();
    }
}
