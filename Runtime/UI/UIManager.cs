using Cysharp.Threading.Tasks;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;

using static LFramework.UI.UIPanelBase;

namespace LFramework.UI
{
    public class UIManager : UnitySingleton<UIManager>, IModule
    {
        protected override void Awake()
        {
            base.Awake();
            if (_isDuplicate) return;
            Init();
        }

        private static Dictionary<Type, UIPanelBase> _panelDic = null;

        /// <summary>
        /// 已注册面板的只读视图。
        /// 注意：<c>UIManager.Init()</c> 之前为 null（组件未 Awake 时）。调用方需自行判空，
        /// 或使用 <see cref="PanelDicOrEmpty"/>。
        /// </summary>
        public static IReadOnlyDictionary<Type, UIPanelBase> PanelDic => _panelDic;

        /// <summary>已注册面板（Init 未执行时返回空字典而非 null，避免调用方 NRE）。</summary>
        public static IReadOnlyDictionary<Type, UIPanelBase> PanelDicOrEmpty =>
            _panelDic ?? EmptyPanelDic;

        private static readonly Dictionary<Type, UIPanelBase> EmptyPanelDic = new Dictionary<Type, UIPanelBase>();

        public static Canvas Canvas { get; private set; } = null;
        public static EventSystem eventSystem { get; private set; } = null;
        public static Camera UICamera { get; private set; } = null;

        private static Dictionary<UIPanelLayer, RectTransform> Layers = null;

        // 面板栈管理
        private static readonly Stack<Type> _panelStack = new Stack<Type>();

        // 每个面板对应的资源句柄：获取即钉住，销毁面板 Dispose 归还（Phase 5 / 5.6 句柄化）
        private static readonly Dictionary<Type, IResourceHandle> _panelHandles = new Dictionary<Type, IResourceHandle>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticStateForNewSession()
        {
            // 关闭 Domain Reload 时清掉上一会话的静态引用（假 null 风险）；Init() 在 Awake 重建（Phase 5 / 5.5）
            _panelDic = null;
            Canvas = null;
            eventSystem = null;
            UICamera = null;
            Layers = null;
        }

        public static RectTransform GetLayer(UIPanelLayer layer)
        {
            return Layers[layer];
        }

        private void Init()
        {
            _panelDic = new Dictionary<Type, UIPanelBase>();
            _panelStack.Clear();
            foreach (var handle in _panelHandles.Values) handle.Dispose();
            _panelHandles.Clear();
            GameObject UIRoot = Instantiate(ResourcesManager.Instance.Load<GameObject>(UISettings.RootPrefabPath));
            Canvas = UIRoot.GetComponentInChildren<Canvas>();
            eventSystem = UIRoot.GetComponentInChildren<EventSystem>();
            UICamera = UIRoot.GetComponentInChildren<Camera>();
            Layers = new Dictionary<UIPanelLayer, RectTransform>();
            foreach (UIPanelLayer layer in Enum.GetValues(typeof(UIPanelLayer)))
            {
                Layers.Add(layer, Canvas.transform.Find(UISettings.GetLayerNodeName(layer)) as RectTransform);
            }
            DontDestroyOnLoad(UIRoot);
        }

        /// <summary>
        /// 打开面板（同步加载），并推入面板栈。
        ///
        /// ⚠️ 打磨台账 B4：这是 **void + .Forget()** 形式，加载失败时调用方**拿不到任何信号** ——
        /// 预制体路径写错、资源被裁剪、并发克隆失败……都只是打一条 Error 就结束，
        /// 业务侧的 `.Forget()` 也不会抛（UniTask 的 Forget 会吞掉异常）。
        /// 需要知道成败时请改用：
        /// - <see cref="OpenAsync{T}"/>（返回面板，失败返回 null，可直接 await 判断）
        /// - <see cref="TryOpen{T}"/>（同步路径的显式成败版）
        /// </summary>
        public void Open<T>(object data = null) where T : UIPanelBase
        {
            OpenInternalAsync<T>(data, pushToStack: true, useSyncLoad: true).Forget();
        }

        /// <summary>
        /// 打开面板（同步加载）并返回是否成功（打磨台账 B4）。
        ///
        /// 与 <see cref="Open{T}"/> 的唯一区别是**能知道成败**。内部仍然异步执行，
        /// 因此这里返回的是"已排入流程"而非"已完成"；真正的失败原因会经
        /// <see cref="LastOpenError"/> 带出。
        ///
        /// 需要"等它真的开好再继续"的场景，请用 <see cref="OpenAsync{T}"/>。
        /// </summary>
        public bool TryOpen<T>(object data = null) where T : UIPanelBase
        {
            return OpenCore<T>(data, pushToStack: true, useSyncLoad: true);
        }

        /// <summary>打开面板但不入栈（同步加载）。失败静默，详见 <see cref="Open{T}"/> 的说明。</summary>
        public void OpenNoStack<T>(object data = null) where T : UIPanelBase
        {
            OpenInternalAsync<T>(data, pushToStack: false, useSyncLoad: true).Forget();
        }

        /// <summary>
        /// 打开面板（异步加载 UniTask 版本），并推入面板栈。
        /// 失败时返回 **null**（打磨台账 B4：这是唯一能让调用方直接判断成败的原有入口）。
        /// </summary>
        public async UniTask<T> OpenAsync<T>(object data = null) where T : UIPanelBase
        {
            return await OpenInternalAsync<T>(data, pushToStack: true, useSyncLoad: false);
        }

        /// <summary>
        /// <see cref="TryOpen{T}"/> 的失败原因（成功打开后被清空）。
        /// 上一次调用若成功，本属性为 null。
        /// </summary>
        public string LastOpenError { get; private set; }

        /// <summary>
        /// TryOpen 的公共实现：启动打开流程并立即返回"是否已排入"。
        /// 失败（预制体缺失）在**克隆阶段同步发生**，因此能在这里拿到结果 ——
        /// 这也是同步加载路径唯一能同步得知成败的窗口。
        /// </summary>
        private bool OpenCore<T>(object data, bool pushToStack, bool useSyncLoad) where T : UIPanelBase
        {
            LastOpenError = null;

            // 已存在（含常驻面板）：不会失败，直接走显示流程
            if (_panelDic.ContainsKey(typeof(T)))
            {
                OpenInternalAsync<T>(data, pushToStack, useSyncLoad).Forget();
                return true;
            }

            // 同步加载路径可以立刻尝试克隆，从而立刻判断"预制体在不在"
            if (useSyncLoad)
            {
                string panelPath = $"{UISettings.PanelPrefabDir}/{typeof(T).Name}";
                if (ResourcesManager.Instance.Load<GameObject>(panelPath) == null)
                {
                    LastOpenError = $"未找到面板预制体: {panelPath}（类型 {typeof(T).Name}）";
                    // 用 Warning 而非 Error：TryOpen 已经通过返回值把失败交给了调用方，
                    // 这不是"无人处理的静默失败"。资源加载失败的 Warning 由 ResourcesLoader 打出。
                    LogManager.Warning($"[UIManager] TryOpen<{typeof(T).Name}> 失败：{LastOpenError}");
                    return false;
                }
            }

            // 预制体存在（或走异步路径）：交给正常流程。
            // 异步路径的失败会在 OpenInternalAsync 内 record 到 LastOpenError。
            OpenTrackedAsync<T>(data, pushToStack, useSyncLoad).Forget();
            return true;
        }

        /// <summary>
        /// 包装 <see cref="OpenInternalAsync{T}"/>，把失败原因写进 <see cref="LastOpenError"/>。
        ///
        /// 为什么需要这层包装：<see cref="OpenInternalAsync{T}"/> 是 async 方法，
        /// 内部捕获异常会中断整个 UniTask，而 .Forget() 把异常吞掉后**什么都没留下**。
        /// 这里统一兜住并记录，让"打开失败"至少有一个可查询的落点。
        /// </summary>
        private async UniTaskVoid OpenTrackedAsync<T>(object data, bool pushToStack, bool useSyncLoad) where T : UIPanelBase
        {
            try
            {
                T panel = await OpenInternalAsync<T>(data, pushToStack, useSyncLoad);
                if (panel == null) LastOpenError = $"打开面板失败: {typeof(T).Name}";
            }
            catch (Exception e)
            {
                LastOpenError = $"打开面板异常: {typeof(T).Name} — {e.Message}";
                LogManager.Error($"[UIManager] Open<{typeof(T).Name}> 异常：{e}");
            }
        }

        /// <summary>
        /// 关闭当前栈顶面板，回到上一个面板
        /// 上一个面板会重新触发 SetAsLastSibling + OnUIEnable + OnShowAnimation
        /// </summary>
        public void Back()
        {
            BackAsync().Forget();
        }

        /// <summary>
        /// 异步回退到栈中上一个面板（带动画）
        /// </summary>
        public async UniTask BackAsync()
        {
            if (_panelStack.Count <= 1)
            {
                LogManager.Warning("[UIManager] BackAsync() failed: no previous panel in stack");
                return;
            }

            // 关闭当前：完整关闭流程（含关闭动画与事件广播）
            Type currentType = _panelStack.Pop();
            if (_panelDic.TryGetValue(currentType, out UIPanelBase currentPanel))
            {
                await CloseInternal(currentPanel, removeFromStack: false);
            }

            // 显示上一个：触发完整显示流程，但不重新初始化数据（保留原状态）
            // 跳过已被销毁的 Type
            while (_panelStack.Count > 0)
            {
                Type previousType = _panelStack.Peek();
                if (_panelDic.TryGetValue(previousType, out UIPanelBase previousPanel))
                {
                    await ShowExistingPanel(previousPanel, null, playAnim: true, reinitData: false);
                    return;
                }
                _panelStack.Pop();
            }
        }
        /// <summary>
        /// 回退到指定面板
        /// </summary>
        public void BackTo<T>() where T : UIPanelBase
        {
            BackToAsync<T>().Forget();
        }

        /// <summary>
        /// 异步回退到指定面板，期间所有中间面板都被关闭
        /// </summary>
        public async UniTask BackToAsync<T>() where T : UIPanelBase
        {
            Type targetType = typeof(T);
            bool found = false;

            while (_panelStack.Count > 0)
            {
                Type top = _panelStack.Peek();
                if (top == targetType)
                {
                    found = true;
                    break;
                }

                if (_panelDic.TryGetValue(top, out UIPanelBase panel))
                {
                    await CloseInternal(panel, removeFromStack: false);
                }
                _panelStack.Pop();
            }

            if (!found)
            {
                LogManager.Error($"[UIManager] BackTo<{targetType.Name}> failed: panel not found in stack");
                return;
            }

            if (_panelDic.TryGetValue(targetType, out UIPanelBase targetPanel))
            {
                await ShowExistingPanel(targetPanel, null, playAnim: true, reinitData: false);
            }
        }

        /// <summary>
        /// 关闭面板（默认行为：未标注 [UIKeepAlive] 的面板会被销毁；标注的仅 SetActive(false)）
        /// </summary>
        public void Close<T>() where T : UIPanelBase
        {
            Close<T>(forceKeep: false);
        }

        /// <summary>
        /// 关闭面板，可强制保活（覆盖类型上的 [UIKeepAlive] 默认行为）
        /// </summary>
        /// <param name="forceKeep">true: 仅 SetActive(false)，保留实例</param>
        public void Close<T>(bool forceKeep) where T : UIPanelBase
        {
            if (_panelDic.TryGetValue(typeof(T), out UIPanelBase panel))
            {
                CloseInternalWithPolicy(panel, forceKeep);
            }
            else
            {
                LogManager.Error($"场景中未找到{typeof(T).Name}面板!!!");
            }
        }

        /// <summary>
        /// 关闭面板（默认行为：未标注 [UIKeepAlive] 的面板会被销毁）
        /// </summary>
        public static void Close(UIPanelBase panel)
        {
            if (panel == null) return;
            CloseInternalWithPolicy(panel, forceKeep: false);
        }

        public static void CloseAll()
        {
            List<UIPanelBase> panelList = new List<UIPanelBase>(_panelDic.Values);
            foreach (UIPanelBase panel in panelList)
            {
                CloseInternalWithPolicy(panel, forceKeep: false);
            }
            _panelStack.Clear();
        }
        public static T Get<T>() where T : UIPanelBase
        {
            if (_panelDic.TryGetValue(typeof(T), out UIPanelBase Panel))
            {
                return Panel as T;
            }
            else
            {
                LogManager.Error($"场景中未找到{typeof(T).Name}面板");
            }
            return default(T);
        }

        public static void DestroyPanel<T>() where T : UIPanelBase
        {
            if (!_panelDic.TryGetValue(typeof(T), out UIPanelBase panel))
            {
                LogManager.Error($"场景中未找到{typeof(T).Name}面板");
            }
            else
            {
                UIManager.DestroyPanel(panel);
            }
        }

        public static void DestroyPanel(UIPanelBase panel)
        {
            if (panel == null) return;
            Type panelType = panel.GetType();

            // 已处于 Closing（例如走 DestroyPanelWithHideAnimationAsync 时已调过 OnUIDisable）
            // 就不要再调一次，避免子类覆写的 OnUIDisable 被触发两遍。
            if (panel.State != UIPanelBase.PanelState.Closing)
            {
                panel.OnUIDisable();
            }

            panel.OnUIDestroy();
            EventCenter.TriggerEvent(UIEventKeys.PanelDestroy(panelType.Name), panel);

            // 修复（打磨台账 B3 同类）：_panelDic 在 UIManager.Init() 之前为 null。
            // 此前这里直接 _panelDic.Remove(...) 会抛 NullReferenceException —— 只要有任何
            // 代码在 UIManager 就绪前碰到面板销毁路径（测试、编辑器工具、启动早期），
            // 就会在毫无提示的地方炸掉。改为判空后跳过登记清理。
            if (_panelDic != null)
            {
                _panelDic.Remove(panelType);
            }
            RemoveTypeFromStack(panelType);

            // 释放句柄：归还面板对预制体的显式引用（Phase 5 / 5.6 句柄化）
            if (_panelHandles.TryGetValue(panelType, out var handle))
            {
                handle.Dispose();
                _panelHandles.Remove(panelType);
            }
        }

        public static void DestroyAll()
        {
            List<UIPanelBase> PanelList = new List<UIPanelBase>(_panelDic.Values);
            foreach (UIPanelBase panel in PanelList)
            {
                UIManager.DestroyPanel(panel);
            }
            _panelDic.Clear();
            _panelStack.Clear();
            foreach (var handle in _panelHandles.Values) handle.Dispose();
            _panelHandles.Clear();
            PanelList.Clear();
        }
        #region 内部实现

        /// <summary>
        /// 根据 [UIKeepAlive] 标注决定关闭后的去向
        /// </summary>
        private static void CloseInternalWithPolicy(UIPanelBase panel, bool forceKeep)
        {
            if (panel == null) return;
            bool keepAlive = forceKeep || HasKeepAlive(panel.GetType());
            if (keepAlive)
            {
                CloseInternal(panel, removeFromStack: true).Forget();
            }
            else
            {
                // 非保活面板同样要先播关闭动画再销毁 —— 否则 [UIKeepAlive] 有无会
                // 决定"关闭动画是否生效"，这是没人能猜到的行为差异。
                DestroyPanelWithHideAnimationAsync(panel).Forget();
            }
        }

        /// <summary>
        /// 播完关闭动画再销毁面板。
        ///
        /// 修复（打磨台账 B1）：<see cref="UIPanelBase.OnHideAnimation"/> 此前全框架零调用，
        /// 面板写了关闭动画永远不会执行；而销毁路径（<see cref="DestroyPanel(UIPanelBase)"/>）
        /// 又直接同步销毁，连动画的插入点都没有。
        ///
        /// 为什么不能把动画放进 DestroyPanel：DestroyPanel 是 public static、被
        /// DestroyAll / 场景卸载等同步路径复用，改成 async 会波及所有调用方。
        /// 因此只在"业务主动关闭"这条路径上先播动画，其它路径保持同步销毁语义。
        /// </summary>
        private static async UniTaskVoid DestroyPanelWithHideAnimationAsync(UIPanelBase panel)
        {
            if (panel == null) return;

            // 顺序与 CloseInternal 保持一致：先隐藏（OnUIDisable）再播关闭动画。
            // 否则"保活面板"与"销毁面板"两条路径的钩子顺序不同，面板作者无从预期。
            panel.OnUIDisable();

            try
            {
                await panel.OnHideAnimation();
            }
            catch (Exception e)
            {
                // 动画异常不能阻断关闭流程，否则面板关不掉
                LogManager.Error($"[UIManager] {panel.GetType().Name} 的关闭动画抛出异常（已忽略，继续销毁）:\n{e} LOGSCAN_EXPECTED", panel.name);
            }

            // 动画期间面板可能已被外部销毁
            if (panel == null) return;

            // DestroyPanel 内部会再调一次 OnUIDisable / OnUIDestroy。
            // OnUIDisable 可重入（只是 SetActive(false) + 置状态），重复调用无副作用。
            DestroyPanel(panel);
        }

        /// <summary>
        /// 关闭面板视觉（不销毁实例）：状态 → 关闭事件 → 关闭动画 → 出栈。
        ///
        /// 修复（打磨台账 B1/B2）：此前 <see cref="UIPanelBase.OnHideAnimation"/> 从未被调用；
        /// 且本方法"看起来是 async 却在第一帧就 return completed"，注释声称"含关闭动画"
        /// 与实现不符。现在真正 await 关闭动画，动画结束后再广播关闭完成事件。
        /// 事件顺序：先 OnUIDisable（面板隐藏）→ 再播动画 → 最后广播 PanelClose，
        /// 保证监听方收到事件时视觉已收尾。
        /// </summary>
        private static async UniTask CloseInternal(UIPanelBase panel, bool removeFromStack)
        {
            if (panel == null) return;

            panel.OnUIDisable();

            try
            {
                await panel.OnHideAnimation();
            }
            catch (Exception e)
            {
                LogManager.Error($"[UIManager] {panel.GetType().Name} 的关闭动画抛出异常（已忽略）:\n{e}", panel.name);
            }

            // 动画期间面板可能已被销毁（例如动画里自己调了 Close）
            if (panel == null) return;
            if (!ReferenceEquals(panel, null) && panel.State == UIPanelBase.PanelState.Destroyed) return;

            EventCenter.TriggerEvent(UIEventKeys.PanelClose(panel.GetType().Name), panel);
            if (removeFromStack)
            {
                RemoveTypeFromStack(panel.GetType());
            }
        }

        private static readonly Dictionary<Type, bool> _keepAliveCache = new Dictionary<Type, bool>();

        private static bool HasKeepAlive(Type t)
        {
            if (_keepAliveCache.TryGetValue(t, out bool cached)) return cached;
            bool has = t.GetCustomAttribute<UIKeepAliveAttribute>(inherit: true) != null;
            _keepAliveCache[t] = has;
            return has;
        }

        /// <summary>
        /// 从面板栈里移除所有等于指定 Type 的项（一般只有 1 个，但保险起见全清）
        /// </summary>
        private static void RemoveTypeFromStack(Type t)
        {
            if (_panelStack.Count == 0) return;
            var keep = new List<Type>(_panelStack.Count);
            // Stack 弹出顺序为 LIFO，倒序后再 Push 才能保留原顺序
            foreach (var x in _panelStack)
            {
                if (x != t) keep.Add(x);
            }
            _panelStack.Clear();
            for (int i = keep.Count - 1; i >= 0; i--)
            {
                _panelStack.Push(keep[i]);
            }
        }

        /// <summary>
        /// 显示一个已存在的面板：统一执行 SetAsLastSibling → (可选)InitPanelData → OnUIEnable → OnShowAnimation
        /// </summary>
        private static async UniTask ShowExistingPanel(UIPanelBase panel, object data, bool playAnim, bool reinitData)
        {
            if (panel == null) return;
            panel.transform.SetAsLastSibling();
            if (reinitData) panel.InitPanelData(data);
            panel.OnUIEnable();
            if (playAnim)
            {
                await panel.OnShowAnimation();
            }
            // 显示完成：推进状态并广播打开完成事件
            panel.State = PanelState.Open;
            EventCenter.TriggerEvent(UIEventKeys.PanelOpen(panel.GetType().Name), panel);
        }

        private async UniTask<T> OpenInternalAsync<T>(object data, bool pushToStack, bool useSyncLoad) where T : UIPanelBase
        {
            if (_panelDic.TryGetValue(typeof(T), out UIPanelBase panel))
            {
                if (pushToStack)
                {
                    RemoveTypeFromStack(typeof(T));
                    _panelStack.Push(typeof(T));
                }
                await ShowExistingPanel(panel, data, playAnim: true, reinitData: true);
                return panel as T;
            }

            var cloned = await ClonePanelAsync<T>(useSyncLoad);
            if (cloned.Panel == null)
            {
                cloned.Handle?.Dispose();
                return null;
            }

            // 并发保护：异步加载期间可能已有另一路注册了同类型面板，此时复用已存在实例
            if (_panelDic.ContainsKey(typeof(T)))
            {
                T existing = _panelDic[typeof(T)] as T;
                // 本次克隆未被采用：归还其句柄引用并销毁克隆实例（已有实例那一路维护了自身句柄）
                cloned.Handle.Dispose();
                Destroy(cloned.Panel.gameObject);

                if (pushToStack)
                {
                    RemoveTypeFromStack(typeof(T));
                    _panelStack.Push(typeof(T));
                }
                await ShowExistingPanel(existing, data, playAnim: true, reinitData: true);
                return existing;
            }
            _panelDic.Add(typeof(T), cloned.Panel);
            _panelHandles[typeof(T)] = cloned.Handle;

            if (pushToStack)
            {
                RemoveTypeFromStack(typeof(T));
                _panelStack.Push(typeof(T));
            }
            cloned.Panel.OnUIAwake();
            await UniTask.Yield();
            cloned.Panel.OnUIStart();
            await ShowExistingPanel(cloned.Panel, data, playAnim: true, reinitData: true);
            return cloned.Panel;
        }

        private async UniTask<(T Panel, IResourceHandle Handle)> ClonePanelAsync<T>(bool useSyncLoad) where T : UIPanelBase
        {
            var panelRootPath = UISettings.PanelPrefabDir;
            var panelSubPath = $"{typeof(T).Name}";
            var panelPath = $"{panelRootPath}/{panelSubPath}";

            GameObject Obj;
            if (useSyncLoad)
            {
                Obj = ResourcesManager.Instance.Load<GameObject>(panelPath);
            }
            else
            {
                Obj = await ResourcesManager.Instance.LoadAsync<GameObject>(panelPath);
            }

            if (Obj == null)
            {
                // 保持 Error 级别：走 OpenAsync 的调用方虽然能拿到 null，
                // 但走 Open<T>/OpenNoStack<T> 的调用方拿不到任何信号（B4），
                // 此时这条 Error 就是唯一线索，不能降级为 Warning。
                // 需要干净日志的测试请用 TryOpen<T> 或 OpenAsync<T> + LogAssert.Expect。
                LogManager.Error($"该路径下:{panelPath}未查找到此面板:{typeof(T).Name}");
                return (null, null);
            }

            // 引用计数：以句柄钉住（Load + 显式引用），销毁面板时 Dispose 归还（Phase 5 / 5.6）
            var handle = ResourcesManager.Instance.Pin<GameObject>(panelPath);

            // 解析 [UILayer] 特性；未标注或读取失败时默认到 Normal 层
            var layerAttr = typeof(T).GetCustomAttribute<UILayerAttribute>(inherit: false);
            RectTransform layer = UIManager.GetLayer(layerAttr?.layer ?? UIPanelLayer.Normal);

            var go = GameObject.Instantiate<GameObject>(Obj, layer);
            go.name = typeof(T).Name;
            var newPanel = go.GetOrAddComponent<T>();
            return (newPanel, handle);
        }

        #endregion

        #region IModule 实现

        public string DisplayName => GetType().Name;
        public UniTask InitializeAsync() => UniTask.CompletedTask;
        public void Dispose() => DestroyAll();

        #endregion
    }
}
