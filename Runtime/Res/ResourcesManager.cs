using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace LFramework
{
    /// <summary>
    /// 资源管理器 - 门面模式，持有 IResourceLoader 引用，委托实际加载逻辑
    /// </summary>
    public class ResourcesManager : SingletonBase<ResourcesManager>, IModule
    {
        private IResourceLoader _loader;

        protected override void Init()
        {
            base.Init();
            _loader = new ResourcesLoader();
        }

        /// <summary>
        /// 替换资源加载器实现（如切换到 Addressables）
        /// </summary>
        public void SetLoader(IResourceLoader loader)
        {
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        }

        public IResourceLoader GetLoader() => _loader;

        #region 加载

        public T Load<T>(string path) where T : UnityEngine.Object
        {
            return _loader.Load<T>(path);
        }

        public T[] LoadAll<T>(string path) where T : UnityEngine.Object
        {
            return _loader.LoadAll<T>(path);
        }

        public void LoadAsync<T>(string path, Action<T> callback) where T : UnityEngine.Object
        {
            _loader.LoadAsync<T>(path, callback);
        }

        public UniTask<T> LoadAsync<T>(string path) where T : UnityEngine.Object
        {
            return _loader.LoadAsync<T>(path);
        }

        #endregion

        #region 释放

        public void Release(string path)
        {
            _loader.Release(path);
        }

        public void AddRef(string path)
        {
            _loader.AddRef(path);
        }

        public void ReleaseRef(string path)
        {
            _loader.ReleaseRef(path);
        }

        public void ReleaseUnused()
        {
            _loader.ReleaseUnused();
        }

        public void ReleaseAll()
        {
            _loader.ReleaseAll();
        }

        public void UnloadUnusedAssets(Action callback = null)
        {
            _loader.UnloadUnusedAssets(callback);
        }

        public void ForceUnload(Action callback = null)
        {
            _loader.UnloadUnusedAssets(() =>
            {
                GC.Collect();
                callback?.Invoke();
            });
        }

        #endregion

        #region 配置与诊断

        public void SetMaxCacheCount(int count)
        {
            _loader.SetMaxCacheCount(count);
        }

        public void SetAutoCleanupInterval(float intervalSeconds)
        {
            _loader.SetAutoCleanupInterval(intervalSeconds);
        }

        public string GetCacheInfo()
        {
            return _loader.GetCacheInfo();
        }

        public int GetCacheCount()
        {
            return _loader.GetCacheCount();
        }

        public bool IsCached(string path)
        {
            return _loader.IsCached(path);
        }

        #endregion

        #region IModule 实现

        public string DisplayName => GetType().Name;
        public UniTask InitializeAsync() => UniTask.CompletedTask;
        public void Tick(float deltaTime) { }
        public void Dispose() => ReleaseAll();

        #endregion
    }
}
