using System;

namespace LFramework
{
    /// <summary>
    /// 资源句柄（Phase 5 / 5.6）：以"获取即钉住、Dispose 即归还"的方式持有资源引用，
    /// 替代手工 AddRef/ReleaseRef 配对。推荐业务优先使用句柄；
    /// 旧 API <see cref="ResourcesManager.AddRef"/> / <see cref="ResourcesManager.ReleaseRef"/>
    /// 已标 [Obsolete]（Phase 5 / 5.6），保留一个版本周期后移除。
    /// 注意 <see cref="ResourcesManager.Release"/> 不在废弃之列——它是隐式持有的归还路径，
    /// 与 Load 配对，是当前的正确用法。
    /// 用法：
    /// <code>
    ///   var handle = ResourcesManager.Instance.Pin&lt;Sprite&gt;("Icons/coin");
    ///   var sprite = handle.Get&lt;Sprite&gt;();
    ///   ... // 使用期间资源不会被容量淘汰（显式钉住）
    ///   handle.Dispose();
    /// </code>
    /// </summary>
    public interface IResourceHandle : IDisposable
    {
        /// <summary>资源路径</summary>
        string Path { get; }

        /// <summary>取资源（缓存命中，不新增引用）</summary>
        T Get<T>() where T : UnityEngine.Object;
    }

    /// <summary>默认实现：Dispose 幂等，只归还自己那一次显式引用。</summary>
    public sealed class ResourceHandle : IResourceHandle
    {
        private readonly ResourcesManager _manager;
        private bool _disposed;

        internal ResourceHandle(ResourcesManager manager, string path)
        {
            _manager = manager;
            Path = path;
        }

        public string Path { get; }

        public T Get<T>() where T : UnityEngine.Object => _manager.Load<T>(Path);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _manager.ReleaseExplicitRef(Path);
        }
    }
}
