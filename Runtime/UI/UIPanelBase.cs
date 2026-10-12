using System;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;

namespace LFramework.UI
{
    public abstract class UIPanelBase : MonoBehaviour
    {
        /// <summary>
        /// 面板生命周期状态机：Opening → Open → (Closing → Open | Destroyed)
        /// Opening:进入显示流程尚未完成动画; Open:显示完成; Closing:进入关闭流程; Destroyed:已销毁
        /// </summary>
        public enum PanelState
        {
            Opening,
            Open,
            Closing,
            Destroyed
        }

        public PanelState State { get; internal set; } = PanelState.Opening;

        private bool _uiComponentsBound;

        // 已登记的数据绑定解绑动作（惰性创建；Phase 5 / 5.7）
        private List<Action> _unbindActions;

        /// <summary>
        /// 生命周期入口，自动按命名约定绑定控件字段
        /// 子类自定义 Awake 时需调用 base.Awake() 才会触发绑定
        /// </summary>
        protected virtual void Awake()
        {
            BindUIComponents();
        }

        /// <summary>
        /// 按命名约定（btn_/img_/text_ 等前缀）自动绑定控件字段，仅执行一次
        /// </summary>
        protected virtual void BindUIComponents()
        {
            if (_uiComponentsBound) return;
            _uiComponentsBound = true;
            UIComponentBinding.Bind(this);
        }

        protected UIPanelBase() { }
        public virtual void OnUIAwake() { }
        public virtual void InitPanelData(object data) { }
        public virtual void OnUIStart() { }
        public virtual void OnUIEnable()
        {
            gameObject.SetActive(true);
            if (State != PanelState.Destroyed) State = PanelState.Opening;
        }
        public virtual void OnUIDisable()
        {
            gameObject.SetActive(false);
            if (State != PanelState.Destroyed) State = PanelState.Closing;
        }
        public virtual void OnUIDestroy()
        {
            // 先退订全部数据绑定，避免面板销毁后仍被 BindableProperty 的委托持有（Phase 5 / 5.7）
            UnbindAll();
            State = PanelState.Destroyed;
            Destroy(gameObject);
        }

        #region 数据绑定（Phase 5 / 5.7，吸收自 XyFrame 的 BindableProperty）

        /// <summary>
        /// 订阅一个 <see cref="BindableProperty{T}"/>：值变化时回调 handler，并登记解绑动作。
        /// 通过本方法建立的订阅会在 <see cref="OnUIDestroy"/> 时自动退订，面板不必手写 -=。
        /// 保活面板（[UIKeepAlive]）隐藏时不销毁，订阅随之保留，重新显示时拿到的仍是最新值。
        /// </summary>
        /// <param name="property">被绑定的属性；null 时忽略</param>
        /// <param name="handler">值变化回调；null 时忽略</param>
        /// <param name="invokeImmediately">true 则立即用当前值回调一次，用于打开界面时同步初值</param>
        protected void Bind<T>(BindableProperty<T> property, Action<T> handler, bool invokeImmediately = false)
        {
            if (property == null || handler == null) return;

            property.Changed += handler;
            if (_unbindActions == null) _unbindActions = new List<Action>();
            _unbindActions.Add(() => property.Changed -= handler);

            if (invokeImmediately) handler(property.Value);
        }

        /// <summary>
        /// 退订一个此前用 <see cref="Bind{T}"/> 建立的订阅（同一属性 + 同一回调）。
        /// 重复调用安全；残留的登记项由 <see cref="UnbindAll"/> 收尾。
        /// </summary>
        protected void Unbind<T>(BindableProperty<T> property, Action<T> handler)
        {
            if (property == null || handler == null) return;
            property.Changed -= handler;
        }

        /// <summary>
        /// 退订本面板建立的全部数据绑定。由 <see cref="OnUIDestroy"/> 自动调用；
        /// 面板若被 UIManager 之外的路径销毁，可自行调用来避免模型持续持有已销毁面板。
        /// </summary>
        public void UnbindAll()
        {
            if (_unbindActions == null) return;
            for (int i = 0; i < _unbindActions.Count; i++)
            {
                _unbindActions[i]();
            }
            _unbindActions.Clear();
        }

        #endregion

        /// <summary>
        /// 面板打开动画钩子，默认无动画立即完成
        /// </summary>
        public virtual UniTask OnShowAnimation()
        {
            return UniTask.CompletedTask;
        }

        /// <summary>
        /// 面板关闭动画钩子，默认无动画立即完成
        /// </summary>
        public virtual UniTask OnHideAnimation()
        {
            return UniTask.CompletedTask;
        }
    }

    /// <summary>
    /// 强类型数据面板基类，避免每个面板手动 cast
    /// 业务实现 InitPanelData(TData data) 即可，数据 null 或类型不匹配时退化为 default(TData)
    /// </summary>
    public abstract class UIPanelBase<TData> : UIPanelBase
    {
        public sealed override void InitPanelData(object data)
        {
            if (data is TData typed)
            {
                InitPanelData(typed);
            }
            else
            {
                InitPanelData(default);
            }
        }

        public virtual void InitPanelData(TData data) { }
    }
}
