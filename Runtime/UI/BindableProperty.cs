using System;
using System.Collections.Generic;

namespace LFramework.UI
{
    /// <summary>
    /// 可绑定属性（Phase 5 / 5.7，吸收自 XyFrame）：值真正变化时触发 Changed。
    /// 数据绑定的核心 —— Model 的字段包一层 BindableProperty，赋值即通知，
    /// 面板不必再手写“改完字段调一次刷新”。
    ///
    /// 与 UIPanelBase.Bind 配合：
    ///   1. Model 侧：public readonly BindableProperty&lt;int&gt; Count = new BindableProperty&lt;int&gt;(0);
    ///   2. 赋值：Count.Value = 3;                        // 自动通知
    ///   3. 面板侧：Bind(model.Count, v =&gt; _countText.text = v.ToString(), invokeImmediately: true);
    ///
    /// 注意 Changed 只在值真正变化时触发（用 EqualityComparer&lt;T&gt;.Default 比较），
    /// 需要“初始化后强制同步一次 UI”时用 ForceNotify() 或 Bind 的 invokeImmediately。
    /// </summary>
    public class BindableProperty<T>
    {
        private T _value;

        /// <summary>值变化事件（参数为新值），仅在值真正变化时触发</summary>
        public event Action<T> Changed;

        public BindableProperty(T initialValue = default(T))
        {
            _value = initialValue;
        }

        /// <summary>当前值：赋值时若与旧值不同则触发 Changed</summary>
        public T Value
        {
            get { return _value; }
            set
            {
                if (EqualityComparer<T>.Default.Equals(_value, value))
                {
                    return;
                }
                _value = value;
                Changed?.Invoke(_value);
            }
        }

        /// <summary>强制触发一次变更（用于初始化后同步 UI）</summary>
        public void ForceNotify()
        {
            Changed?.Invoke(_value);
        }
    }
}
