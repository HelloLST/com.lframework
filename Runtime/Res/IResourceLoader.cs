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
        /// 加载目录下所有同类型资源（打磨台账 A1）。
        ///
        /// **会产生持有**：每个资源各登记一次隐式持有，必须通过
        /// <see cref="ReleaseAllFrom"/>（或逐个 <see cref="Release"/>）归还。
        /// 旧实现绕过缓存直接返回，这批资源在框架眼里不存在 —— 无法淘汰、无法计数。
        /// </summary>
        T[] LoadAll<T>(string path) where T : UnityEngine.Object;

        /// <summary>
        /// 归还 <see cref="LoadAll{T}"/> 在某目录下登记的所有资源各一次持有，返回归还条数（打磨台账 A1）。
        ///
        /// 后端不支持批量登记（如 Addressables 的 LoadAll 尚未实现）时返回 0 即可。
        /// </summary>
        int ReleaseAllFrom(string folderPath);

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

        /// <summary>
        /// 只读地从缓存取资源：**不新增持有、不触发加载**（打磨台账 A4）。
        ///
        /// 与 <see cref="Load{T}"/> 的区别：Load 会产生一次隐式持有（需 Release 归还），
        /// TryPeek 只是"把已在内存里的东西读出来"。句柄持有资源期间再取对象应走这个，
        /// 否则每次取用都会凭空多一份永不归还的持有。
        ///
        /// 未命中返回 false。**后端实现注意**：Addressables 后端按需实现，
        /// 不缓存也就无从 peek，返回 false 即可（句柄回退到 LoadAsync 路径）。
        /// </summary>
        bool TryPeek<T>(string path, out T asset) where T : UnityEngine.Object;
    }
}
