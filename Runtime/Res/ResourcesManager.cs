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

        /// <summary>
        /// 加载器访问点：惰性创建，任何取用路径都拿得到可用实例。
        /// Phase 9：原实现只在 <see cref="Init"/> 里赋值，一旦有代码绕过 <c>Instance</c>
        /// 拿到未初始化的对象（例如反射构造的模块实例），这里就会是 null 并一路炸到 ReleaseAll。
        /// </summary>
        private IResourceLoader Loader
        {
            get
            {
                if (_loader == null) _loader = new ResourcesLoader();
                return _loader;
            }
        }

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

        public IResourceLoader GetLoader() => Loader;

        #region 加载

        public T Load<T>(string path) where T : UnityEngine.Object
        {
            return Loader.Load<T>(path);
        }

        public T[] LoadAll<T>(string path) where T : UnityEngine.Object
        {
            return Loader.LoadAll<T>(path);
        }

        public void LoadAsync<T>(string path, Action<T> callback) where T : UnityEngine.Object
        {
            Loader.LoadAsync<T>(path, callback);
        }

        public UniTask<T> LoadAsync<T>(string path) where T : UnityEngine.Object
        {
            return Loader.LoadAsync<T>(path);
        }

        #endregion

        #region 释放

        public void Release(string path)
        {
            Loader.Release(path);
        }

        /// <summary>
        /// 【已废弃 · Phase 5 / 5.6】增加显式引用计数。
        /// 请改用 <see cref="Pin{T}"/> 返回的 <see cref="IResourceHandle"/>：
        /// 手工 AddRef/ReleaseRef 配对容易漏，句柄 Dispose 会自动归还，配对关系也可静态追踪。
        /// </summary>
        [Obsolete("请改用 ResourcesManager.Pin<T>() 返回的 IResourceHandle；手工 AddRef/ReleaseRef 配对易漏。旧 API 将在 1.x 后移除。")]
        public void AddRef(string path)
        {
            Loader.AddRef(path);
        }

        /// <summary>
        /// 【已废弃 · Phase 5 / 5.6】减少显式引用计数。
        /// 请改用 <see cref="Pin{T}"/> 返回的 <see cref="IResourceHandle"/>。
        /// </summary>
        [Obsolete("请改用 ResourcesManager.Pin<T>() 返回的 IResourceHandle；手工 AddRef/ReleaseRef 配对易漏。旧 API 将在 1.x 后移除。")]
        public void ReleaseRef(string path)
        {
            Loader.ReleaseRef(path);
        }

        /// <summary>
        /// 归还一次显式引用 —— <see cref="ResourceHandle.Dispose"/> 专用通道。
        /// 单独开这个方法是为了让句柄实现自身不触发上方两个 [Obsolete] 门面的弃用警告；
        /// 业务代码不要直接调用，请走 <see cref="Pin{T}"/> + Dispose。
        /// </summary>
        internal void ReleaseExplicitRef(string path)
        {
            Loader.ReleaseRef(path);
        }

        /// <summary>
        /// 以句柄方式钉住资源（Phase 5 / 5.6）：内部完成 Load + 显式引用 +1，
        /// 句柄 Dispose 时归还——比手工 AddRef/ReleaseRef 配对更难漏。
        /// 句柄释放后资源仍可能留在缓存（隐式持有），由容量/LRU 策略管理。
        /// </summary>
        public IResourceHandle Pin<T>(string path) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("path 为空", nameof(path));
            Loader.Load<T>(path);   // 确保已缓存（隐式持有 +1；已加载则为缓存命中）
            Loader.AddRef(path);    // 句柄显式钉住 +1
            return new ResourceHandle(this, path);
        }

        public void ReleaseUnused()
        {
            Loader.ReleaseUnused();
        }

        public void ReleaseAll()
        {
            Loader.ReleaseAll();
        }

        public void UnloadUnusedAssets(Action callback = null)
        {
            Loader.UnloadUnusedAssets(callback);
        }

        public void ForceUnload(Action callback = null)
        {
            Loader.UnloadUnusedAssets(() =>
            {
                GC.Collect();
                callback?.Invoke();
            });
        }

        #endregion

        #region 配置与诊断

        public void SetMaxCacheCount(int count)
        {
            Loader.SetMaxCacheCount(count);
        }

        public void SetAutoCleanupInterval(float intervalSeconds)
        {
            Loader.SetAutoCleanupInterval(intervalSeconds);
        }

        public string GetCacheInfo()
        {
            return Loader.GetCacheInfo();
        }

        public int GetCacheCount()
        {
            return Loader.GetCacheCount();
        }

        public bool IsCached(string path)
        {
            return Loader.IsCached(path);
        }

        #endregion

        #region IModule 实现

        public string DisplayName => GetType().Name;
        public UniTask InitializeAsync() => UniTask.CompletedTask;
        public void Dispose() => ReleaseAll();

        #endregion
    }
}
