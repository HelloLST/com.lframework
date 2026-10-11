using System;
using System.Collections.Generic;
using UnityEngine;

namespace LFramework.Architecture
{
    /// <summary>
    /// 共享变量资产基类：把跨系统共享的运行时数据从 MonoBehaviour 字段中剥离出来，
    /// 变成一个可以被任意多方引用的资产。生产者只管写，消费者只管订阅，双方互不认识。
    ///
    /// 三条必须理解的约定，不理解就会踩坑：
    /// 1. InitialValue 是设计期配置值，随资产序列化落盘，策划可在 Inspector 中直接改，不需要程序介入；
    /// 2. Value 是运行时值，标记 NonSerialized，Play 模式下的读写不会污染资产文件，
    ///    避免"跑一遍游戏，血量资产被改成 0 并被提交进 Git"这类事故；
    /// 3. 资产加载 / 反序列化时运行时值自动从初始值重置，保证每次进入场景都是干净状态。
    /// </summary>
    public abstract class ScriptableVariable<T> : ScriptableObject, ISerializationCallbackReceiver
    {
        [SerializeField]
        [Tooltip("设计期初始值：随资产持久化，进入 Play 时作为运行时值的起点")]
        private T _initialValue;

        [NonSerialized] private T _runtimeValue;

        /// <summary>
        /// 值变化广播。只有值真正发生改变时才触发，避免每帧写入相同值导致 UI 无意义重刷。
        /// </summary>
        public event Action<T> OnValueChanged;

        /// <summary>设计期初始值（只读）</summary>
        public T InitialValue => _initialValue;

        /// <summary>运行时值。写入时若与当前值相等则不广播。</summary>
        public T Value
        {
            get => _runtimeValue;
            set
            {
                // EqualityComparer<T>.Default 对值类型不装箱，避免高频写入产生 GC
                if (EqualityComparer<T>.Default.Equals(_runtimeValue, value)) return;
                _runtimeValue = value;
                OnValueChanged?.Invoke(_runtimeValue);
            }
        }

        public void SetValue(T value) => Value = value;

        /// <summary>
        /// 重置回设计期初始值并强制广播一次。关卡重开 / 场景切换时调用，
        /// 保证不会有上一局的残留数据渗透到新一局。
        /// </summary>
        public void ResetToInitial()
        {
            _runtimeValue = _initialValue;
            OnValueChanged?.Invoke(_runtimeValue);
        }

        /// <summary>
        /// 清空所有订阅者。关闭 Domain Reload 时 SO 上的委托会跨 Play 会话残留，
        /// 需要时手动调用可避免"上一次运行的监听器还挂着"的幽灵回调。
        /// </summary>
        public void ClearListeners() => OnValueChanged = null;

        protected virtual void OnEnable()
        {
            _runtimeValue = _initialValue;
            OnValueChanged = null;
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize() { }

        // 注意：反序列化回调可能不在主线程，这里只做纯字段赋值，禁止调用任何 Unity API
        void ISerializationCallbackReceiver.OnAfterDeserialize() => _runtimeValue = _initialValue;

        public override string ToString() => $"{name}({typeof(T).Name}) = {_runtimeValue}";
    }

    [CreateAssetMenu(menuName = "LFramework/Variables/Int", fileName = "IntVariable", order = 0)]
    public class IntVariable : ScriptableVariable<int>
    {
        /// <summary>在当前值基础上增减，计分 / 计数场景最常用</summary>
        public void ApplyChange(int amount) => Value += amount;
    }

    [CreateAssetMenu(menuName = "LFramework/Variables/Float", fileName = "FloatVariable", order = 1)]
    public class FloatVariable : ScriptableVariable<float>
    {
        public void ApplyChange(float amount) => Value += amount;

        /// <summary>按上下限裁剪后写入，血量 / 进度这类有边界的值用它</summary>
        public void SetClamped(float value, float min, float max) => Value = Mathf.Clamp(value, min, max);
    }

    [CreateAssetMenu(menuName = "LFramework/Variables/Bool", fileName = "BoolVariable", order = 2)]
    public class BoolVariable : ScriptableVariable<bool>
    {
        public void Toggle() => Value = !Value;
    }

    [CreateAssetMenu(menuName = "LFramework/Variables/String", fileName = "StringVariable", order = 3)]
    public class StringVariable : ScriptableVariable<string>
    {
    }
}
