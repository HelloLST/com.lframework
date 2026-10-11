using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;

namespace LFramework
{
    /// <summary>
    /// 基于 Resources.Load 的资源加载器实现。
    /// 引用计数契约（Phase 1 / P0-1 修复后）：
    ///   1. Load/LoadAsync 首次加载产生一次隐式持有（RefCount=1），由 Release(path) 归还；
    ///      缓存命中不新增隐式引用。用完不 Release 只会造成缓存偏大，但不会死循环。
    ///   2. AddRef/ReleaseRef 为显式引用对，用于"借出资源期间钉住"；显式钉住且非 GameObject
    ///      的资源不会被容量淘汰（强制卸载会破坏使用中的贴图/音频等资产）。
    ///   3. 容量上限是硬约束：超限时淘汰最久未使用的可安全回收项；若全部不可安全回收，
    ///      允许暂时超限，绝不允许清理循环无限自旋。
    /// Phase 5 / 5.3：清理与卸载改 UniTask 驱动，不再依赖 MonoManager 协程（本类已与 MonoManager 解耦）。
    /// </summary>
    public class ResourcesLoader : IResourceLoader
    {
        private readonly Dictionary<string, ResourceCacheItem> _cache = new Dictionary<string, ResourceCacheItem>();
        private readonly LinkedList<string> _lruList = new LinkedList<string>();
        private readonly Dictionary<string, ResourceRequest> _loadingRequests = new Dictionary<string, ResourceRequest>();

        private int _maxCacheCount = 100;
        private float _autoCleanupInterval = 60f;
        private CancellationTokenSource _cleanupCts;

        public ResourcesLoader()
        {
            StartAutoCleanup();
        }

        public T Load<T>(string path) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(path))
            {
                LogManager.Error("[ResourcesLoader] Load failed: path is null or empty");
                return null;
            }

            if (_cache.TryGetValue(path, out ResourceCacheItem cachedItem))
            {
                UpdateLRU(path);
                cachedItem.LastAccessTime = DateTime.Now;
                return cachedItem.Asset as T;
            }

            T asset = Resources.Load<T>(path);
            if (asset == null)
            {
                LogManager.Warning($"[ResourcesLoader] Load failed: {path}");
                return null;
            }

            AddToCache(path, asset);
            return asset;
        }

        public T[] LoadAll<T>(string path) where T : UnityEngine.Object
        {
            return Resources.LoadAll<T>(path);
        }

        public void LoadAsync<T>(string path, Action<T> callback) where T : UnityEngine.Object
        {
            LoadAsyncInternal<T>(path, callback).Forget();
        }

        public async UniTask<T> LoadAsync<T>(string path) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(path))
            {
                LogManager.Error("[ResourcesLoader] LoadAsync failed: path is null or empty");
                return null;
            }

            if (_cache.TryGetValue(path, out ResourceCacheItem cachedItem))
            {
                UpdateLRU(path);
                cachedItem.LastAccessTime = DateTime.Now;
                return cachedItem.Asset as T;
            }

            if (_loadingRequests.TryGetValue(path, out ResourceRequest existingRequest))
            {
                await existingRequest.ToUniTask();
                return existingRequest.asset as T;
            }

            ResourceRequest request = Resources.LoadAsync<T>(path);
            _loadingRequests[path] = request;
            await request.ToUniTask();
            _loadingRequests.Remove(path);

            if (request.asset == null)
            {
                LogManager.Warning($"[ResourcesLoader] LoadAsync failed: {path}");
                return null;
            }

            AddToCache(path, request.asset);
            return request.asset as T;
        }

        private async UniTaskVoid LoadAsyncInternal<T>(string path, Action<T> callback) where T : UnityEngine.Object
        {
            T asset = await LoadAsync<T>(path);
            callback?.Invoke(asset);
        }

        public void UnloadUnusedAssets(Action callback = null)
        {
            UnloadUnusedAssetsAsync(callback).Forget();
        }

        private async UniTaskVoid UnloadUnusedAssetsAsync(Action callback)
        {
            ReleaseUnused();
            AsyncOperation asyncOp = Resources.UnloadUnusedAssets();
            await UniTask.WaitUntil(() => asyncOp.isDone);
            callback?.Invoke();
        }

        public void ReleaseAll()
        {
            if (_cache.Count == 0) return;
            foreach (var item in _cache.Values)
            {
                if (item.Asset is GameObject) continue;
                if (item.Asset is AssetBundle bundle)
                    bundle.Unload(true);
                else
                    Resources.UnloadAsset(item.Asset);
            }
            _cache.Clear();
            _lruList.Clear();
        }

        #region 缓存管理

        public void SetMaxCacheCount(int count) => _maxCacheCount = Mathf.Max(1, count);
        public void SetAutoCleanupInterval(float seconds)
        {
            _autoCleanupInterval = Mathf.Max(10f, seconds);
            StartAutoCleanup();
        }
        public int GetCacheCount() => _cache.Count;
        public bool IsCached(string path) => _cache.ContainsKey(path);

        /// <summary>
        /// 归还一次隐式加载引用（与 Load/LoadAsync 配对）。
        /// 若仍有显式引用（AddRef 未配对 ReleaseRef）钉着，则只扣减、不卸载。
        /// </summary>
        public void Release(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            if (_cache.TryGetValue(path, out ResourceCacheItem item))
            {
                if (item.RefCount <= 1)
                    ReleaseResource(path, item);   // 只剩隐式持有 → 归还并卸载
                else
                    item.ReleaseRef();             // 显式引用仍在 → 只归还隐式引用
            }
        }

        public void AddRef(string path)
        {
            if (_cache.TryGetValue(path, out ResourceCacheItem item))
                item.AddRef();
        }

        public void ReleaseRef(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            if (_cache.TryGetValue(path, out ResourceCacheItem item))
            {
                item.ReleaseRef();
                if (item.RefCount <= 0)
                    ReleaseResource(path, item);   // 最后一个显式引用归还 → 释放
            }
        }

        private void AddToCache(string path, UnityEngine.Object asset)
        {
            if (_cache.Count >= _maxCacheCount)
                EvictLRU();

            var item = new ResourceCacheItem(asset, path);
            _cache[path] = item;
            _lruList.AddLast(path);
        }

        private void UpdateLRU(string path)
        {
            var node = _lruList.Find(path);
            if (node != null)
            {
                _lruList.Remove(node);
                _lruList.AddLast(path);
            }
        }

        /// <summary>
        /// 容量收紧淘汰（硬约束）：淘汰最久未使用的一项。
        /// 可淘汰条件（满足其一）：
        ///   - GameObject 资源：只移出缓存、不卸载（实例由持有方管理，见 ReleaseResource）；
        ///   - 未被显式 AddRef 钉住的资源（RefCount&lt;=1，即仅剩隐式持有）：
        ///     隐式持有允许被容量压力回收，需要时会重新 Load。
        /// 被 AddRef 钉住的非 GameObject 资源跳过——强制 UnloadAsset 会破坏使用中的资产。
        /// 返回是否成功淘汰一项；全部不可回收时返回 false，调用方必须据此终止循环。
        /// </summary>
        private bool EvictLRU()
        {
            int attempts = _lruList.Count;
            while (attempts-- > 0 && _lruList.Count > 0)
            {
                string oldestPath = _lruList.First.Value;
                _lruList.RemoveFirst();

                if (_cache.TryGetValue(oldestPath, out ResourceCacheItem item))
                {
                    bool explicitPinned = item.RefCount > 1;
                    bool isGameObject = item.Asset is GameObject;
                    if (explicitPinned && !isGameObject)
                    {
                        // 显式钉住且不可安全卸载：移到队尾，继续找下一个
                        _lruList.AddLast(oldestPath);
                        continue;
                    }
                    ReleaseResource(oldestPath, item);
                    return true;
                }
                // 缓存与链表不一致的悬挂项：直接丢弃继续
            }
            return false;
        }

        private void ReleaseResource(string path, ResourceCacheItem item)
        {
            _cache.Remove(path);
            _lruList.Remove(path);

            if (item.Asset is GameObject)
            {
                // GameObject 由对象池管理
            }
            else if (item.Asset is AssetBundle bundle)
            {
                bundle.Unload(true);
            }
            else
            {
                Resources.UnloadAsset(item.Asset);
            }
        }

        public void ReleaseUnused()
        {
            List<string> toRelease = new List<string>();
            foreach (var kvp in _cache)
            {
                if (kvp.Value.RefCount <= 0)
                    toRelease.Add(kvp.Key);
            }
            foreach (string path in toRelease)
            {
                if (_cache.TryGetValue(path, out ResourceCacheItem item))
                    ReleaseResource(path, item);
            }
        }

        private void StartAutoCleanup()
        {
            StopAutoCleanup();
            _cleanupCts = new CancellationTokenSource();
            AutoCleanupLoop(_cleanupCts.Token).Forget();
        }

        private async UniTaskVoid AutoCleanupLoop(CancellationToken ct)
        {
            while (true)
            {
                await UniTask.Delay(TimeSpan.FromSeconds(_autoCleanupInterval), DelayType.DeltaTime, PlayerLoopTiming.Update, ct);
                ReleaseUnused();

                // 容量收紧：EvictLRU 返回 false 说明没有可安全淘汰的项，
                // 必须终止循环 —— 否则缓存超限时这里会无限自旋卡死主线程（P0-1 修复）
                int guard = _cache.Count;
                while (_cache.Count >= _maxCacheCount && guard-- > 0)
                {
                    if (!EvictLRU()) break;
                }
            }
        }

        public void StopAutoCleanup()
        {
            if (_cleanupCts != null)
            {
                _cleanupCts.Cancel();
                _cleanupCts.Dispose();
                _cleanupCts = null;
            }
        }

        public string GetCacheInfo()
        {
            return $"[ResourcesLoader] Cached: {_cache.Count}/{_maxCacheCount}, AutoCleanup: {_autoCleanupInterval}s";
        }

        #endregion
    }
}
