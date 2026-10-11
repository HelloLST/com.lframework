using System;
using Cysharp.Threading.Tasks;

namespace LFramework
{
    /// <summary>
    /// 资源加载器接口 - 为不同资源后端(Resources/Addressables/AB)提供统一抽象
    /// </summary>
    public interface IResourceLoader
    {
        /// <summary>
        /// 同步加载资源
        /// 注意：Addressables 后端不支持同步加载，调用时会抛出 NotSupportedException，请改用 LoadAsync
        /// </summary>
        T Load<T>(string path) where T : UnityEngine.Object;

        /// <summary>
        /// 异步加载资源（UniTask 版本，推荐）
        /// </summary>
        UniTask<T> LoadAsync<T>(string path) where T : UnityEngine.Object;

        /// <summary>
        /// 异步加载资源（回调版本，兼容旧 API）
        /// </summary>
        void LoadAsync<T>(string path, Action<T> callback) where T : UnityEngine.Object;

        /// <summary>
        /// 加载目录下所有同类型资源
        /// </summary>
        T[] LoadAll<T>(string path) where T : UnityEngine.Object;

        /// <summary>
        /// 卸载未使用的资源
        /// </summary>
        void UnloadUnusedAssets(Action callback = null);

        /// <summary>
        /// 释放所有已加载的资源
        /// </summary>
        void ReleaseAll();

        /// <summary>
        /// 增加资源的引用计数（供 UI 等持有方调用）
        /// </summary>
        void AddRef(string path);

        /// <summary>
        /// 减少资源的引用计数
        /// </summary>
        void ReleaseRef(string path);

        /// <summary>
        /// 释放指定路径资源
        /// </summary>
        void Release(string path);

        /// <summary>
        /// 释放所有引用计数为 0 的缓存资源
        /// </summary>
        void ReleaseUnused();

        /// <summary>
        /// 设置缓存上限
        /// </summary>
        void SetMaxCacheCount(int count);

        /// <summary>
        /// 设置自动清理间隔（秒）
        /// </summary>
        void SetAutoCleanupInterval(float intervalSeconds);

        /// <summary>
        /// 缓存信息字符串（诊断用）
        /// </summary>
        string GetCacheInfo();

        /// <summary>
        /// 当前缓存数量
        /// </summary>
        int GetCacheCount();

        /// <summary>
        /// 指定路径是否已缓存
        /// </summary>
        bool IsCached(string path);
    }
}
