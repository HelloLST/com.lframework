using System;
using System.Collections.Generic;
using UnityEngine;

namespace LFramework
{
    /// <summary>
    /// 运行时会话重置中枢（Phase 7）。
    ///
    /// 背景：关闭 Domain Reload 时，静态状态会跨 Play 会话残留；框架统一在进 Play 时重置。
    /// 但声明在【开放泛型类型】上的 [RuntimeInitializeOnLoadMethod] 不会被引擎为闭合类型调用
    /// （已实测 Unity 2022.3.62f3c1：SingletonBase / UnitySingleton / ScriptableConfigCache 均受影响）。
    ///
    /// 方案：泛型类型在静态构造函数里向本类注册重置委托（注册时立即执行一次，
    /// 保证晚注册的也能当场复位并挂好事件）；本类为非泛型，引擎回调保证被执行，
    /// 每次进 Play 遍历已注册委托。注册列表跨会话保留——DR-off 下静态构造函数不会重跑，
    /// 清空列表会让后续会话失去重置能力。
    ///
    /// 限制：仅在主线程会话边界调用；注册发生在主线程首次类型使用时，天然串行。
    /// </summary>
    internal static class RuntimeSessionReset
    {
        private static readonly List<Action> _resets = new List<Action>();

        /// <summary>注册一个会话重置委托并立即执行一次（晚注册的静态状态需当场复位）。</summary>
        internal static void Register(Action reset)
        {
            if (reset == null) return;
            _resets.Add(reset);
            reset();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetAllForNewSession()
        {
            for (int i = 0; i < _resets.Count; i++) _resets[i]();
        }
    }
}