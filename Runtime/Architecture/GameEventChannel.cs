using System;
using System.Collections.Generic;
using UnityEngine;

namespace LFramework.Architecture
{
    /// <summary>
    /// 无参事件通道。发送方和接收方都只认识这个资产，彼此互不认识，实现跨系统零硬引用通信。
    ///
    /// 与现有 EventCenter 的分工（两者共存，不互相取代）：
    /// - EventCenter：字符串 key，适合临时的、动态生成 key 的事件（例如按面板类型名派发）；
    /// - 事件通道：资产引用，编译期可追踪、IDE 里能 Find References、策划可在 Inspector 直接连线，
    ///   适合"得分变化""游戏结束"这类稳定的业务事件。稳定事件一律走通道，别再堆字符串常量。
    /// </summary>
    [CreateAssetMenu(menuName = "LFramework/Events/Void Channel", fileName = "VoidChannel", order = 0)]
    public class VoidEventChannel : ScriptableObject
    {
        private readonly List<Action> _listeners = new List<Action>();

        [TextArea(2, 5)]
        [SerializeField]
        [Tooltip("这个通道在什么时候被触发？写给队友和三个月后的自己看")]
        private string _description;

        public string Description => _description;

        public int ListenerCount => _listeners.Count;

        /// <summary>触发事件。倒序遍历，允许监听器在回调中安全退订自己。</summary>
        public void Raise()
        {
            for (int i = _listeners.Count - 1; i >= 0; i--)
            {
                if (i >= _listeners.Count) continue;
                try
                {
                    _listeners[i]?.Invoke();
                }
                catch (Exception e)
                {
                    // 单个监听器炸掉不应该连累其余监听器，把异常就地拦下并定位到资产
                    Debug.LogError($"[EventChannel:{name}] 监听器抛出异常：{e}", this);
                }
            }
        }

        /// <summary>订阅并返回令牌，Dispose 即退订。推荐用令牌管理，避免订阅退订不对称。</summary>
        public IDisposable Subscribe(Action listener)
        {
            if (listener == null) return EmptySubscription.Instance;
            if (!_listeners.Contains(listener)) _listeners.Add(listener);
            return new ChannelSubscription(() => Unsubscribe(listener));
        }

        public void Unsubscribe(Action listener)
        {
            if (listener == null) return;
            _listeners.Remove(listener);
        }

        // SO 的运行时监听列表绝不能跨会话残留，加载与卸载时都清空。
        // 声明为 protected virtual 而非 private：子类若定义同名方法会静默隐藏基类实现，
        // 导致清理逻辑不执行，这类问题在 Unity 消息机制下极难排查。
        protected virtual void OnEnable() => _listeners.Clear();

        protected virtual void OnDisable() => _listeners.Clear();
    }

    /// <summary>
    /// 带参事件通道基类。派生具体类型后即可在 Inspector 中创建资产。
    /// </summary>
    public abstract class GameEventChannel<T> : ScriptableObject
    {
        private readonly List<Action<T>> _listeners = new List<Action<T>>();

        [TextArea(2, 5)]
        [SerializeField]
        [Tooltip("这个通道在什么时候被触发？参数代表什么含义？")]
        private string _description;

        public string Description => _description;

        public int ListenerCount => _listeners.Count;

        public void Raise(T payload)
        {
            for (int i = _listeners.Count - 1; i >= 0; i--)
            {
                if (i >= _listeners.Count) continue;
                try
                {
                    _listeners[i]?.Invoke(payload);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[EventChannel:{name}] 监听器抛出异常：{e}", this);
                }
            }
        }

        public IDisposable Subscribe(Action<T> listener)
        {
            if (listener == null) return EmptySubscription.Instance;
            if (!_listeners.Contains(listener)) _listeners.Add(listener);
            return new ChannelSubscription(() => Unsubscribe(listener));
        }

        public void Unsubscribe(Action<T> listener)
        {
            if (listener == null) return;
            _listeners.Remove(listener);
        }

        protected virtual void OnEnable() => _listeners.Clear();

        protected virtual void OnDisable() => _listeners.Clear();
    }

    [CreateAssetMenu(menuName = "LFramework/Events/Int Channel", fileName = "IntChannel", order = 1)]
    public sealed class IntEventChannel : GameEventChannel<int> { }

    [CreateAssetMenu(menuName = "LFramework/Events/Float Channel", fileName = "FloatChannel", order = 2)]
    public sealed class FloatEventChannel : GameEventChannel<float> { }

    [CreateAssetMenu(menuName = "LFramework/Events/Bool Channel", fileName = "BoolChannel", order = 3)]
    public sealed class BoolEventChannel : GameEventChannel<bool> { }

    [CreateAssetMenu(menuName = "LFramework/Events/String Channel", fileName = "StringChannel", order = 4)]
    public sealed class StringEventChannel : GameEventChannel<string> { }
}
