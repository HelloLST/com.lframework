using UnityEngine;
namespace LFramework
{
    public class ResourceCacheItem
    {
        public UnityEngine.Object Asset { get; }
        public string Path { get; }
        public System.DateTime LastAccessTime { get; set; }
        public int RefCount { get; set; }

        /// <summary>
        /// 隐式持有次数（Load / LoadAsync 各产生一次，Release(path) 归还）——打磨台账 A4。
        ///
        /// 存在理由：<c>RefCount</c> 是"隐式 + 显式"的合计，无法区分两者。
        /// 于是 <c>Release(path)</c> 只能靠 <c>RefCount &lt;= 1</c> 猜"还有没有显式钉住"，
        /// 也就无法判断"这次 Release 到底有没有对应的 Load"——
        /// 漏 Release、重复 Release、路径拼错，全都静默通过，资源收支永远对不上。
        /// 拆出独立计数后，Release 可以精确判定配对关系并显式报警。
        /// </summary>
        public int ImplicitCount { get; private set; }

        public ResourceCacheItem(UnityEngine.Object asset, string path)
        {
            // RefCount 从 1 起 = Load/LoadAsync 的隐式持有，由 Release(path) 归还；
            // AddRef/ReleaseRef 是叠加其上的显式引用对（Phase 1 / P0-1 所有权语义）
            Asset = asset; Path = path; LastAccessTime = System.DateTime.Now;
            RefCount = 1;
            ImplicitCount = 1;
        }

        public void AddRef() => RefCount++;
        public void ReleaseRef() => RefCount = Mathf.Max(0, RefCount - 1);

        /// <summary>再增加一次隐式持有（同一路径被再次 Load 且缓存命中时调用）。</summary>
        public void AddImplicit()
        {
            ImplicitCount++;
            RefCount++;
        }

        /// <summary>
        /// 归还一次隐式持有。返回 false 表示**没有可归还的隐式持有**（重复 Release）。
        /// 注意：不在这里动 RefCount —— 由调用方决定整体语义（见 ResourcesLoader.Release）。
        /// </summary>
        public bool TryReleaseImplicit()
        {
            if (ImplicitCount <= 0) return false;
            ImplicitCount--;
            return true;
        }
    }
}
