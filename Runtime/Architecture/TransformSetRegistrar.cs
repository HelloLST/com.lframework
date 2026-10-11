using UnityEngine;

namespace LFramework.Architecture
{
    /// <summary>
    /// 运行时集合登记器：挂在预制体上，对象启用时把自己登记进集合，禁用时摘除。
    ///
    /// 这个小组件是干掉 GameObject.FindGameObjectWithTag("Player") 的关键：
    /// 主角预制体挂上它并指向 PlayerSet 资产，任何需要主角的系统只要引用同一个资产，
    /// 就能拿到主角 Transform，既不需要遍历场景，也不需要知道主角叫什么、挂什么 Tag。
    ///
    /// 注意登记的是自己，摘除也是自己，所以预制体丢进任何空场景都能独立工作，不依赖场景层级结构。
    /// </summary>
    [AddComponentMenu("LFramework/Transform Set Registrar")]
    public class TransformSetRegistrar : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("要登记进的运行时集合资产")]
        private TransformRuntimeSet _runtimeSet;

        [SerializeField]
        [Tooltip("留空则登记自身 Transform")]
        private Transform _target;

        private Transform Target => _target != null ? _target : transform;

        private void OnEnable()
        {
            if (_runtimeSet == null)
            {
                Debug.LogWarning($"[TransformSetRegistrar] {name} 未指定运行时集合，登记被跳过", this);
                return;
            }
            _runtimeSet.Add(Target);
        }

        private void OnDisable()
        {
            if (_runtimeSet == null) return;
            _runtimeSet.Remove(Target);
        }
    }
}
