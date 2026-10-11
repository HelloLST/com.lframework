using UnityEngine;
using UnityEngine.Events;

namespace LFramework.Architecture
{
    /// <summary>
    /// 事件通道监听器：挂在任意 GameObject 上，把通道触发转成 Inspector 里连好的 UnityEvent 响应。
    ///
    /// 存在的意义是让策划和美术也能参与逻辑装配：
    /// 程序只负责在合适的时机 Raise 通道，"事件发生后播个音效、弹个特效、亮一下按钮"
    /// 这类表现层反应由策划自己在 Inspector 拖，不需要为每个需求改代码重编译。
    /// </summary>
    [AddComponentMenu("LFramework/Game Event Listener")]
    public class GameEventListener : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("要监听的事件通道资产")]
        private VoidEventChannel _channel;

        [SerializeField]
        [Tooltip("通道触发时执行的响应")]
        private UnityEvent _response;

        private void OnEnable()
        {
            if (_channel == null)
            {
                Debug.LogWarning($"[GameEventListener] {name} 未指定事件通道，该监听器不会生效", this);
                return;
            }
            _channel.Subscribe(OnEventRaised);
        }

        private void OnDisable()
        {
            if (_channel == null) return;
            _channel.Unsubscribe(OnEventRaised);
        }

        private void OnEventRaised() => _response?.Invoke();
    }
}
