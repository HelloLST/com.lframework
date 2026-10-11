using System;

namespace LFramework.Architecture
{
    /// <summary>
    /// 订阅令牌：Dispose 时执行退订动作，并保证只退订一次。
    /// 用法：把 Subscribe 的返回值存起来，在 OnDisable / OnUIDisable 中 Dispose，
    /// 就不会再出现"订阅写在 A 钩子、退订漏在 B 钩子"的生命周期不对称问题。
    /// </summary>
    internal sealed class ChannelSubscription : IDisposable
    {
        private Action _unsubscribe;

        public ChannelSubscription(Action unsubscribe)
        {
            _unsubscribe = unsubscribe;
        }

        public void Dispose()
        {
            Action unsub = _unsubscribe;
            if (unsub == null) return;
            _unsubscribe = null;
            unsub();
        }
    }

    /// <summary>
    /// 空订阅令牌：订阅参数非法时返回它，让调用方无需判空即可安全 Dispose。
    /// </summary>
    internal sealed class EmptySubscription : IDisposable
    {
        public static readonly EmptySubscription Instance = new EmptySubscription();

        private EmptySubscription() { }

        public void Dispose() { }
    }
}
