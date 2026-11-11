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

        /// <summary>
        /// LoadAll 的分组登记：目录 → 这次加载登记过的缓存 key 列表（打磨台账 A1）。
        ///
        /// 为什么必须单独记一份，而不能靠"folder/资源名"推导路径：
        /// <c>Resources.LoadAll</c> 是**递归**的，运行时又拿不到资源的真实子路径
        /// （AssetDatabase 只在编辑器可用）。递归目录下推导出的 key 与真实路径不符，
        /// 靠它归还就会漏。分组记录的是"这次到底登记了哪几条"，与路径推导彻底解耦。
        /// </summary>
        private readonly Dictionary<string, List<string>> _loadAllGroups = new Dictionary<string, List<string>>();

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
                // 打磨台账 A4：缓存命中也要计一次隐式持有。
                // 旧实现不计数，导致"Load 两次、Release 一次"时资源被立刻卸载，
                // 而调用方以为自己还持有一份 —— 典型的悬空引用。
                cachedItem.AddImplicit();
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
            if (string.IsNullOrEmpty(path))
            {
                LogManager.Error("[ResourcesLoader] LoadAll failed: path is null or empty");
                return Array.Empty<T>();
            }
            T[] assets = Resources.LoadAll<T>(path);
            if (assets == null || assets.Length == 0)
            {
                LogManager.Warning($"[ResourcesLoader] LoadAll 未加载到任何资源: {path}");
                return assets ?? Array.Empty<T>();
            }

            // 打磨台账 A1：旧实现直接 `return Resources.LoadAll<T>(path)`，资源**不进缓存、
            // 不计引用**。业务拿到一批图后，这批资源在框架眼里根本不存在 ——
            // IsCached 恒 false，LRU 看不见，ReleaseUnused 管不到，容量策略也摸不着。
            // 结果是想按容量回收回收不掉，只能靠 Resources.UnloadUnusedAssets 全量兜底。
            //
            // 现在逐个登记进缓存：每个资源各计一次隐式持有。
            //
            // ⚠️ 路径推导的固有限制（必须知道，否则会误用）：
            // `Resources.LoadAll` 是**递归**的，而运行时没有 API 能拿到某个资源的真实子路径
            // （编辑器的 AssetDatabase.GetAssetPath 在打包后不可用）。因此这里只能用
            // `folder/资源名` 作为登记 key —— 对"目录下直接放资源"的常见用法是准确的，
            // 对递归加载则只是 best-effort（真实路径可能是 folder/sub/资源名）。
            // 正因如此，**归还必须走 <see cref="ReleaseAllFrom"/> 的分组登记**，
            // 不能依赖自己拼路径 —— 分组记录的是"这次 LoadAll 到底登记了哪几条"，与路径推导无关。
            string folder = path.TrimEnd('/');
            List<string> group = null;

            for (int i = 0; i < assets.Length; i++)
            {
                T asset = assets[i];
                if (asset == null) continue;

                string key = RegisterLoadedAsset(folder, asset);
                if (key == null) continue;

                group ??= new List<string>();
                if (!group.Contains(key)) group.Add(key);
            }

            if (group != null)
            {
                // 同一目录重复 LoadAll 时追加到已有分组（持有是累加的，归还也要能累加）
                if (_loadAllGroups.TryGetValue(folder, out List<string> existing))
                {
                    for (int i = 0; i < group.Count; i++)
                        if (!existing.Contains(group[i])) existing.Add(group[i]);
                }
                else
                {
                    _loadAllGroups[folder] = group;
                }
            }

            return assets;
        }

        /// <summary>
        /// 拼出某个资源在 <see cref="LoadAll{T}"/> 之后**登记的缓存路径**（best-effort）。
        ///
        /// ⚠️ 这只在"资源直接位于该目录下"时准确。递归加载时真实路径可能更深，
        /// 此处无法得知（运行时无 API）。**归还请用 <see cref="ReleaseAllFrom"/>** ——
        /// 它走分组登记，不依赖路径推导。
        /// </summary>
        public static string MakeAssetCachePath(string folderPath, UnityEngine.Object asset)
        {
            if (string.IsNullOrEmpty(folderPath)) return asset != null ? asset.name : string.Empty;
            if (asset == null) return folderPath.TrimEnd('/');

            return folderPath.TrimEnd('/') + "/" + asset.name;
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
                cachedItem.AddImplicit();   // 同 Load：缓存命中计一次隐式持有（A4）
                return cachedItem.Asset as T;
            }

            if (_loadingRequests.TryGetValue(path, out ResourceRequest existingRequest))
            {
                await existingRequest.ToUniTask();
                var existingAsset = existingRequest.asset;
                if (existingAsset == null)
                {
                    // 打磨台账 A4 附带：并发等待失败时也要有 Warning，
                    // 旧实现只有"首个加载者"那条路径会告警，等待者静默拿到 null。
                    LogManager.Warning($"[ResourcesLoader] LoadAsync failed (并发等待方): {path}");
                    return null;
                }
                // 并发场景下缓存可能已被首个加载者写入，这里补记隐式持有
                if (_cache.TryGetValue(path, out ResourceCacheItem waiterItem))
                {
                    waiterItem.AddImplicit();
                }
                return existingAsset as T;
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
            _loadAllGroups.Clear();
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
        /// 只读地从缓存取资源，**不新增任何持有、不触发加载**（打磨台账 A4）。
        /// 供 <see cref="ResourceHandle.Get{T}"/> 使用：句柄已经持有资源，
        /// 再读一次不应变成第二份持有。
        /// 未命中返回 false。
        /// </summary>
        public bool TryPeek<T>(string path, out T asset) where T : UnityEngine.Object
        {
            asset = null;
            if (string.IsNullOrEmpty(path)) return false;
            if (!_cache.TryGetValue(path, out ResourceCacheItem item)) return false;

            asset = item.Asset as T;
            // 读取不算"使用"吗？算 —— 更新 LRU，但**不动引用计数**。
            UpdateLRU(path);
            item.LastAccessTime = DateTime.Now;
            return asset != null;
        }

        /// <summary>
        /// 归还一次隐式加载引用（与 Load / LoadAsync 配对）。
        /// 若仍有显式引用（AddRef 未配对 ReleaseRef）钉着，则只扣减、不卸载。
        ///
        /// 打磨台账 A4：旧实现有两类静默失败 ——
        ///   1) 路径不在缓存里（拼错 / 从未加载 / 已被淘汰）→ 直接 `return`，调用方毫不知情；
        ///   2) 重复 Release（隐式持有已归零）→ 继续扣减，把显式引用也一并吃掉。
        /// 两者都会让资源收支永远对不上，而 `GetCacheCount` 看不出任何异常。
        /// 现在区分「未缓存」与「重复归还」两种情况并显式告警。
        /// </summary>
        public void Release(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                LogManager.Warning("[ResourcesLoader] Release failed: path is null or empty");
                return;
            }

            // 打磨台账 A1：目录形式（LoadAll 用过的 path）→ 整体归还该目录下的登记项。
            // 业务逐个拼子路径既啰嗦又容易漏，漏一个就是永久泄漏。
            if (!_cache.ContainsKey(path) && ReleaseFolder(path.TrimEnd('/'), warnIfEmpty: false) > 0)
                return;

            if (!_cache.TryGetValue(path, out ResourceCacheItem item))
            {
                // 不在缓存：可能是路径拼错、从未 Load、或已被 LRU 淘汰。
                // 淘汰后调用 Release 是**合法**的（资源本来就不在了），所以用 Warning 而非 Error，
                // 但必须留痕 —— 否则"我明明 Release 了"这种争论无法定案。
                LogManager.Warning(
                    $"[ResourcesLoader] Release 了未缓存的资源: {path}" +
                    "（可能路径拼错、从未 Load，或已被容量淘汰）。本次调用无效果。");
                return;
            }

            // 隐式持有已归零 = 重复 Release。此时不能再扣 RefCount，
            // 否则会把别人的显式 AddRef 吃光，导致资源被提前卸载。
            if (!item.TryReleaseImplicit())
            {
                LogManager.Warning(
                    $"[ResourcesLoader] 重复 Release（隐式持有已为 0）: {path}，当前 RefCount={item.RefCount}。" +
                    "本次调用无效果；请检查 Load 与 Release 是否成对。");
                return;
            }

            item.ReleaseRef();   // 归还这一次隐式持有对应的计数

            if (item.RefCount <= 0)
            {
                ReleaseResource(path, item);   // 已无任何持有 → 卸载
            }
        }

        public void AddRef(string path)
        {
            if (_cache.TryGetValue(path, out ResourceCacheItem item))
                item.AddRef();
        }

        /// <summary>
        /// 归还 <see cref="LoadAll{T}"/> 在 <paramref name="folderPath"/> 下登记的所有资源各一次持有，
        /// 返回实际归还的条数（打磨台账 A1）。
        ///
        /// 为什么需要专用方法而不是让调用方自己拼路径：一个目录通常几十个资源，
        /// 逐个 Release 既啰嗦又容易漏；而漏掉的持有永远不会归零，资源就永久留在缓存里。
        /// </summary>
        public int ReleaseAllFrom(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath)) return 0;
            return ReleaseFolder(folderPath.TrimEnd('/'), warnIfEmpty: true);
        }

        /// <summary>
        /// 归还某个目录下所有登记项各一次隐式持有，返回归还条数。
        ///
        /// 优先走 <see cref="_loadAllGroups"/> 的分组登记（精确，且对递归加载也成立）；
        /// 分组里没有时才回退到"按前缀匹配直接子项"（兼容手动拼路径登记的场景）。
        /// </summary>
        private int ReleaseFolder(string folder, bool warnIfEmpty)
        {
            if (string.IsNullOrEmpty(folder)) return 0;

            var matches = new List<string>();

            // 1. 分组登记：LoadAll 精确记录过的那批 key
            if (_loadAllGroups.TryGetValue(folder, out List<string> group))
            {
                matches.AddRange(group);
                _loadAllGroups.Remove(folder);
            }

            // 2. 回退：按前缀匹配的直接子项（不含更深层级）
            string prefix = folder + "/";
            foreach (var kvp in _cache)
            {
                if (!kvp.Key.StartsWith(prefix, StringComparison.Ordinal)) continue;
                if (kvp.Key.IndexOf('/', prefix.Length) >= 0) continue;
                if (!matches.Contains(kvp.Key)) matches.Add(kvp.Key);
            }

            if (matches.Count == 0)
            {
                if (warnIfEmpty)
                {
                    LogManager.Warning(
                        $"[ResourcesLoader] ReleaseAllFrom 未找到任何登记项: {folder}。" +
                        "请确认传的是 LoadAll 用过的目录路径（不带尾部斜杠），且资源未被容量淘汰。");
                }
                return 0;
            }

            int released = 0;
            for (int i = 0; i < matches.Count; i++)
            {
                string key = matches[i];
                if (!_cache.TryGetValue(key, out ResourceCacheItem item)) continue;

                if (!item.TryReleaseImplicit())
                {
                    LogManager.Warning($"[ResourcesLoader] ReleaseAllFrom: {key} 的隐式持有已为 0，跳过。");
                    continue;
                }

                item.ReleaseRef();
                released++;

                if (item.RefCount <= 0) ReleaseResource(key, item);
            }

            return released;
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

        /// <summary>
        /// 登记一个已加载的资源（<see cref="LoadAll{T}"/> 用），返回它在缓存里的 key（失败返回 null）。
        /// 每个资源计一次隐式持有。
        /// 与 <see cref="AddToCache"/> 的区别：已存在的条目**累加持有**而不是覆盖 ——
        /// 重复 LoadAll 同一目录应产生 N 次隐式持有、需 N 次 Release 归还，
        /// 直接覆盖会让之前的持有凭空消失。
        /// </summary>
        private string RegisterLoadedAsset(string folder, UnityEngine.Object asset)
        {
            if (string.IsNullOrEmpty(folder) || asset == null) return null;

            string key = MakeAssetCachePath(folder, asset);
            if (string.IsNullOrEmpty(key)) return null;

            if (_cache.TryGetValue(key, out ResourceCacheItem existing))
            {
                // 已被单点 Load 缓存过（或上一次 LoadAll 登记过）：同一资源，再计一次隐式持有
                existing.AddImplicit();
                UpdateLRU(key);
                existing.LastAccessTime = DateTime.Now;
                return key;
            }

            AddToCache(key, asset);
            return key;
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
