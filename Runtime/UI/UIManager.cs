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
        /// 已注册面板的只读视图
        /// </summary>
        public static IReadOnlyDictionary<Type, UIPanelBase> PanelDic => _panelDic;

        public static Canvas Canvas { get; private set; } = null;
        public static EventSystem eventSystem { get; private set; } = null;
        public static Camera UICamera { get; private set; } = null;

        private static Dictionary<UIPanelLayer, RectTransform> Layers = null;

        // 面板栈管理
        private static readonly Stack<Type> _panelStack = new Stack<Type>();

        // 每个面板对应的资源句柄：获取即钉住，销毁面板 Dispose 归还（Phase 5 / 5.6 句柄化）
        private static readonly Dictionary<Type, IResourceHandle> _panelHandles = new Dictionary<Type, IResourceHandle>();

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
        /// 打开面板（同步加载），并推入面板栈
        /// 注意：Addressables 后端不支持同步加载，使用 Addressables 时请改用 OpenAsync
        /// </summary>
        public void Open<T>(object data = null) where T : UIPanelBase
        {
            OpenInternalAsync<T>(data, pushToStack: true, useSyncLoad: true).Forget();
        }

        /// <summary>
        /// 打开面板（异步加载 UniTask 版本），并推入面板栈
        /// </summary>
        public async UniTask<T> OpenAsync<T>(object data = null) where T : UIPanelBase
        {
            return await OpenInternalAsync<T>(data, pushToStack: true, useSyncLoad: false);
        }

        /// <summary>
        /// 打开面板但不入栈（用于辅助面板等）
        /// </summary>
        public void OpenNoStack<T>(object data = null) where T : UIPanelBase
        {
            OpenInternalAsync<T>(data, pushToStack: false, useSyncLoad: true).Forget();
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
            panel.OnUIDisable();
            panel.OnUIDestroy();
            EventCenter.TriggerEvent(UIEventKeys.PanelDestroy(panelType.Name), panel);
            _panelDic.Remove(panelType);
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
                DestroyPanel(panel);
            }
        }

        /// <summary>
        /// 仅关闭面板视觉，不销毁实例
        /// </summary>
        private static UniTask CloseInternal(UIPanelBase panel, bool removeFromStack)
        {
            if (panel == null) return UniTask.CompletedTask;
            panel.OnUIDisable();
            EventCenter.TriggerEvent(UIEventKeys.PanelClose(panel.GetType().Name), panel);
            if (removeFromStack)
            {
                RemoveTypeFromStack(panel.GetType());
            }
            return UniTask.CompletedTask;
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
