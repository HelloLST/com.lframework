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
            State = PanelState.Destroyed;
            Destroy(gameObject);
        }

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
