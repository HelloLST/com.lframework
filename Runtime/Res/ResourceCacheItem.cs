using UnityEngine;
namespace LFramework
{
    public class ResourceCacheItem
    {
        public UnityEngine.Object Asset { get; }
        public string Path { get; }
        public System.DateTime LastAccessTime { get; set; }
        public int RefCount { get; set; }
        public ResourceCacheItem(UnityEngine.Object asset, string path)
        {
            // RefCount 从 1 起 = Load/LoadAsync 的隐式持有，由 Release(path) 归还；
            // AddRef/ReleaseRef 是叠加其上的显式引用对（Phase 1 / P0-1 所有权语义）
            Asset = asset; Path = path; LastAccessTime = System.DateTime.Now; RefCount = 1;
        }
        public void AddRef() => RefCount++;
        public void ReleaseRef() => RefCount = Mathf.Max(0, RefCount - 1);
    }
}