using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

#if USE_ADDRESSABLES
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using System.Collections.Generic;
#endif

namespace LFramework
{
    /// <summary>
    /// 基于 Addressables 的资源加载器骨架
    /// 启用需要：1) 安装 com.unity.addressables 包；2) Player Settings 中加入 USE_ADDRESSABLES 宏；3) 在启动时调用 ResourcesManager.Instance.SetLoader(new AddressablesLoader())
    /// 注意：Addressables 后端不支持同步加载，Load<T> 会抛 NotSupportedException，业务方必须改用 LoadAsync
    /// </summary>
    public class AddressablesLoader : IResourceLoader
    {
#if USE_ADDRESSABLES
        private readonly Dictionary<string, AsyncOperationHandle> _handles = new Dictionary<string, AsyncOperationHandle>();
        private readonly Dictionary<string, int> _refCounts = new Dictionary<string, int>();

        public T Load<T>(string path) where T : UnityEngine.Object
        {
            throw new NotSupportedException("[AddressablesLoader] Addressables backend does not support synchronous Load. Use LoadAsync instead.");
        }

        public async UniTask<T> LoadAsync<T>(string path) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogError("[AddressablesLoader] LoadAsync failed: path is null or empty");
                return null;
            }

            if (_handles.TryGetValue(path, out AsyncOperationHandle existing))
            {
                if (existing.IsDone)
                {
                    if (existing.Status != AsyncOperationStatus.Succeeded)
                    {
                        // 失败请求：释放 handle 并从表移除，避免 AsyncOperation 资源泄漏
                        _handles.Remove(path);
                        Addressables.Release(existing);
                        return null;
                    }
                    return existing.Result as T;
                }

                try
                {
                    await existing.Task;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[AddressablesLoader] LoadAsync failed: {path} ({e.Message})");
                    _handles.Remove(path);
                    Addressables.Release(existing);
                    return null;
                }

                if (existing.Status != AsyncOperationStatus.Succeeded)
                {
                    _handles.Remove(path);
                    Addressables.Release(existing);
                    return null;
                }
                return existing.Result as T;
            }

            AsyncOperationHandle<T> handle = Addressables.LoadAssetAsync<T>(path);
            _handles[path] = handle;

            try
            {
                await handle.Task;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AddressablesLoader] LoadAsync failed: {path} ({e.Message})");
                _handles.Remove(path);
                Addressables.Release(handle);
                return null;
            }

            if (handle.Status != AsyncOperationStatus.Succeeded)
            {
                Debug.LogWarning($"[AddressablesLoader] LoadAsync failed: {path}");
                // 失败请求：释放 handle，避免 AsyncOperation 资源泄漏
                _handles.Remove(path);
                Addressables.Release(handle);
                return null;
            }

            return handle.Result;
        }

        public void LoadAsync<T>(string path, Action<T> callback) where T : UnityEngine.Object
        {
            LoadAsyncInternal(path, callback).Forget();
        }

        private async UniTaskVoid LoadAsyncInternal<T>(string path, Action<T> callback) where T : UnityEngine.Object
        {
            T asset = await LoadAsync<T>(path);
            callback?.Invoke(asset);
        }

        public T[] LoadAll<T>(string path) where T : UnityEngine.Object
        {
            // TODO: 使用 Addressables.LoadAssetsAsync<T>(label) 实现按 Label 加载
            throw new NotImplementedException("[AddressablesLoader] LoadAll not implemented; use label-based loading via Addressables.LoadAssetsAsync");
        }

        public void AddRef(string path)
        {
            if (!_handles.ContainsKey(path)) return;
            _refCounts.TryGetValue(path, out int count);
            _refCounts[path] = count + 1;
        }

        public void ReleaseRef(string path)
        {
            if (!_refCounts.TryGetValue(path, out int count)) return;
            count--;
            if (count <= 0)
            {
                _refCounts.Remove(path);
                Release(path);
            }
            else
            {
                _refCounts[path] = count;
            }
        }

        public void Release(string path)
        {
            if (_handles.TryGetValue(path, out AsyncOperationHandle handle))
            {
                Addressables.Release(handle);
                _handles.Remove(path);
                _refCounts.Remove(path);
            }
        }

        public void UnloadUnusedAssets(Action callback = null)
        {
            // Addressables 通过引用计数自动卸载，未引用的资源在 Release 时即被卸载
            callback?.Invoke();
        }

        public void ReleaseAll()
        {
            foreach (var kvp in _handles)
            {
                Addressables.Release(kvp.Value);
            }
            _handles.Clear();
            _refCounts.Clear();
        }

        public void ReleaseUnused() { }

        public void SetMaxCacheCount(int count) { }

        public void SetAutoCleanupInterval(float intervalSeconds) { }

        public string GetCacheInfo() => $"[AddressablesLoader] Handles: {_handles.Count}";

        public int GetCacheCount() => _handles.Count;

        public bool IsCached(string path) => _handles.ContainsKey(path);
#else
        public T Load<T>(string path) where T : UnityEngine.Object
        {
            throw new NotSupportedException("[AddressablesLoader] USE_ADDRESSABLES 宏未启用");
        }

        public UniTask<T> LoadAsync<T>(string path) where T : UnityEngine.Object
        {
            throw new NotSupportedException("[AddressablesLoader] USE_ADDRESSABLES 宏未启用");
        }

        public void LoadAsync<T>(string path, Action<T> callback) where T : UnityEngine.Object
        {
            throw new NotSupportedException("[AddressablesLoader] USE_ADDRESSABLES 宏未启用");
        }

        public T[] LoadAll<T>(string path) where T : UnityEngine.Object
        {
            throw new NotSupportedException("[AddressablesLoader] USE_ADDRESSABLES 宏未启用");
        }

        public void UnloadUnusedAssets(Action callback = null) { }
        public void ReleaseAll() { }
        public void AddRef(string path) { }
        public void ReleaseRef(string path) { }
        public void Release(string path) { }
        public void ReleaseUnused() { }
        public void SetMaxCacheCount(int count) { }
        public void SetAutoCleanupInterval(float intervalSeconds) { }
        public string GetCacheInfo() => "[AddressablesLoader] USE_ADDRESSABLES 宏未启用";
        public int GetCacheCount() => 0;
        public bool IsCached(string path) => false;
#endif
    }
}
