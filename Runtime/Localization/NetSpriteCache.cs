using System.Collections.Generic;
using UnityEngine;

namespace LFramework
{
    /// <summary>
    /// 网络精灵缓存（H3 修复）。
    ///
    /// 存在理由：<see cref="ImageLocalization"/> 早期的网络图片缓存是一个「只进不出的静态字典」，
    /// 每下载一张图就多一个条目，进程不退出就永远不释放。而 Sprite 和它背后的 Texture2D 都是
    /// **运行时创建的 Unity 对象**，GC 回收不了 —— 必须显式 <c>Destroy</c> 才会把显存还回去。
    /// 长会话（多语言切图、商城/活动页反复打开）下内存曲线只涨不落，且没有任何报错。
    ///
    /// 现在的语义：
    /// - 每个 url 一个条目，带一个**持有数**（= 当前正在显示这张图的实例数量）；
    /// - 条目总数超过 <see cref="MaxCount"/> 时，从「持有数为 0」的条目里挑最久未使用的淘汰；
    /// - 淘汰时同时销毁 Sprite 与（由本缓存持有的）Texture2D，真正释放显存；
    /// - **持有数 &gt; 0 的条目永不被淘汰** —— 正在显示的内容不会突然变白。
    ///
    /// 公开这个类型（而不是藏在 <see cref="ImageLocalization"/> 私有静态字段里）的原因有两个：
    /// 一是业务可以在切场景时调用 <see cref="Clear"/> 主动把显存压下去；
    /// 二是缓存淘汰策略必须能被测试直接驱动，否则又会变成「写了但没验证」。
    /// </summary>
    public static class NetSpriteCache
    {
        private sealed class Entry
        {
            public Sprite Sprite;

            /// <summary>由本缓存负责销毁的贴图；<c>null</c> 表示贴图归外部所有，淘汰时不碰。</summary>
            public Texture2D OwnedTexture;

            /// <summary>当前持有数（正在显示这张图的实例数量）。</summary>
            public int Holds;

            /// <summary>最近一次被使用时的逻辑时钟值，用于 LRU 淘汰。</summary>
            public long LastUseTick;
        }

        /// <summary>缓存条目上限。&lt;= 0 表示不限制（保留旧行为，仅供确实需要全部常驻的场景使用）。</summary>
        public static int MaxCount { get; set; } = 32;

        private static readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>();
        private static long _tick;

        /// <summary>当前缓存条目数。</summary>
        public static int Count => _entries.Count;

        /// <summary>
        /// 只读查询：命中返回精灵但**不加持有**。用于「先看看有没有」的探测路径。
        /// </summary>
        public static bool TryPeek(string url, out Sprite sprite)
        {
            sprite = null;
            if (string.IsNullOrEmpty(url)) return false;

            if (!_entries.TryGetValue(url, out Entry entry)) return false;

            // 精灵被外部销毁了（例如手工 Destroy 过），条目已经失效，顺手清掉
            if (entry.Sprite == null)
            {
                _entries.Remove(url);
                return false;
            }

            sprite = entry.Sprite;
            return true;
        }

        /// <summary>
        /// 原子地「查缓存 + 加一次持有」。调用方必须配对 <see cref="Release"/>。
        /// 缓存里没有、或精灵已被销毁时返回 false。
        /// </summary>
        public static bool TryAcquire(string url, out Sprite sprite)
        {
            if (!TryPeek(url, out sprite)) return false;

            _entries[url].Holds++;
            _entries[url].LastUseTick = ++_tick;
            Trim();
            return true;
        }

        /// <summary>
        /// 对已登记的 url 补一次持有（并发去重时，非首个请求者用这个）。
        /// 条目不存在（已被淘汰/清空）时返回 false，由调用方决定是否重新登记。
        /// </summary>
        public static bool Acquire(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            if (!_entries.TryGetValue(url, out Entry entry)) return false;
            if (entry.Sprite == null) { _entries.Remove(url); return false; }

            entry.Holds++;
            entry.LastUseTick = ++_tick;
            Trim();
            return true;
        }

        /// <summary>
        /// 释放一次持有。持有数归零后该条目变为可淘汰，并立即按上限做一次淘汰。
        /// </summary>
        public static void Release(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            if (!_entries.TryGetValue(url, out Entry entry)) return;   // 已被淘汰/清空，属正常

            if (entry.Holds <= 0)
            {
                // 重复释放是真实缺陷（少 Release 会泄漏，多 Release 会提前毁掉正在用的图），
                // 与其静默不如留痕
                LogManager.Warning($"[NetSpriteCache] 重复释放网络精灵 url={url}（持有数已是 0）");
                return;
            }

            entry.Holds--;
            entry.LastUseTick = ++_tick;
            Trim();
        }

        /// <summary>
        /// 登记一张新下载的图（持有数为 0）。
        /// <paramref name="ownsTexture"/> 为 true 时，淘汰/清空会连带销毁它的贴图 ——
        /// 网络下载的贴图是运行时对象，不销毁就永远占显存。
        /// </summary>
        public static void Register(string url, Sprite sprite, bool ownsTexture = true)
        {
            RegisterCore(url, sprite, ownsTexture, 0);
        }

        /// <summary>
        /// 登记并直接占用一次持有（持有数从 1 起）。
        ///
        /// 与「先 Register 再 Acquire」的区别在于**原子性**：Register 之后到 Acquire 之前，
        /// 条目持有数还是 0 —— 若此时 <see cref="Trim"/> 触发，刚登记的图会被自己淘汰掉
        /// （它恰好是唯一的「无人持有」条目），随后 Acquire 找不到条目，
        /// 调用方拿到一张已被销毁的精灵。
        /// 本方法把「登记」与「占位」合并，天然免疫这个竞态。
        /// </summary>
        public static void RegisterAndHold(string url, Sprite sprite, bool ownsTexture = true)
        {
            RegisterCore(url, sprite, ownsTexture, 1);
        }

        private static void RegisterCore(string url, Sprite sprite, bool ownsTexture, int initialHolds)
        {
            if (string.IsNullOrEmpty(url) || sprite == null) return;

            if (_entries.TryGetValue(url, out Entry existing))
            {
                // 已有条目（并发请求重复登记）：保留原有持有数，换掉资源并释放旧的
                Texture2D oldTexture = existing.OwnedTexture;
                Sprite oldSprite = existing.Sprite;
                existing.Sprite = sprite;
                existing.OwnedTexture = ownsTexture ? sprite.texture : null;
                existing.LastUseTick = ++_tick;
                existing.Holds += initialHolds;
                DestroyResource(oldSprite, oldTexture);
                Trim();
                return;
            }

            _entries[url] = new Entry
            {
                Sprite = sprite,
                OwnedTexture = ownsTexture ? sprite.texture : null,
                Holds = initialHolds,
                LastUseTick = ++_tick,
            };
            Trim();
        }

        /// <summary>查询某个 url 当前的持有数（诊断 / 测试用）。</summary>
        public static int GetHolds(string url)
        {
            if (string.IsNullOrEmpty(url)) return 0;
            return _entries.TryGetValue(url, out Entry entry) ? entry.Holds : 0;
        }

        /// <summary>
        /// 清空缓存并销毁所有由本缓存持有的精灵与贴图。
        /// 切场景、内存告警、以及关闭 Domain Reload 时的会话重置都会用到。
        /// </summary>
        public static void Clear()
        {
            foreach (Entry entry in _entries.Values)
            {
                DestroyResource(entry.Sprite, entry.OwnedTexture);
            }
            _entries.Clear();
            _tick = 0;
        }

        /// <summary>
        /// 按 <see cref="MaxCount"/> 淘汰最久未使用且**无人持有**的条目。
        /// 全部条目都在使用时不淘汰（宁可短暂超限，也不能把正在显示的图销毁掉）。
        /// </summary>
        public static void Trim()
        {
            if (MaxCount <= 0) return;

            while (_entries.Count > MaxCount)
            {
                string victim = null;
                long oldestTick = long.MaxValue;

                foreach (KeyValuePair<string, Entry> kvp in _entries)
                {
                    Entry entry = kvp.Value;
                    if (entry.Holds > 0) continue;                 // 正在使用，跳过
                    if (entry.LastUseTick >= oldestTick) continue;
                    oldestTick = entry.LastUseTick;
                    victim = kvp.Key;
                }

                if (victim == null) return;                        // 全都在用，暂时超限

                Entry removed = _entries[victim];
                _entries.Remove(victim);
                DestroyResource(removed.Sprite, removed.OwnedTexture);
            }
        }

        private static void DestroyResource(Sprite sprite, Texture2D ownedTexture)
        {
            if (sprite != null) DestroyObject(sprite);
            if (ownedTexture != null) DestroyObject(ownedTexture);
        }

        private static void DestroyObject(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Object.Destroy(target);
            else Object.DestroyImmediate(target);
        }
    }
}
