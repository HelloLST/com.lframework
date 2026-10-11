using System;
using System.Collections.Generic;
using UnityEngine;

namespace LFramework
{
    /// <summary>
    /// 模块清单 - ScriptableObject 资产，策划可在 Inspector 中增删/排序模块，无需重编译。
    /// 由 ModuleManager 读取，完成类型解析 + 拓扑排序 + 有序初始化。
    /// </summary>
    [CreateAssetMenu(menuName = "LFramework/Module Manifest", fileName = "ModuleManifest", order = 0)]
    public class ModuleManifest : ScriptableObject
    {
        [SerializeField]
        [Tooltip("框架启动时自动创建的模块列表。拖拽调整顺序即可调整初始化优先级；dependsOn 声明的依赖会被拓扑排序自动满足。")]
        private List<ModuleEntry> modules = new List<ModuleEntry>();

        /// <summary>只读视图</summary>
        public IReadOnlyList<ModuleEntry> Modules => modules;

        /// <summary>
        /// 代码创建默认清单(运行时兜底用，不保存到磁盘)。
        /// 包含 LFramework 全部核心管理器，按依赖关系拓扑排序。
        /// 建议正式项目通过 CreateAssetMenu 创建 SO 资产并在 Inspector 中编辑。
        /// </summary>
        public static ModuleManifest CreateDefault()
        {
            var manifest = CreateInstance<ModuleManifest>();
            manifest.modules = new List<ModuleEntry>
            {
                new ModuleEntry
                {
                    displayName = "TimerMgr",
                    typeName = "LFramework.TimerMgr",
                    enabled = true
                },
                new ModuleEntry
                {
                    displayName = "LogManager",
                    typeName = "LFramework.LogManager",
                    enabled = true
                },
                new ModuleEntry
                {
                    displayName = "AudioManager",
                    typeName = "LFramework.AudioManager",
                    enabled = true
                },
                new ModuleEntry
                {
                    displayName = "MonoManager",
                    typeName = "LFramework.MonoManager",
                    enabled = true
                },
                new ModuleEntry
                {
                    displayName = "ResourcesManager",
                    typeName = "LFramework.ResourcesManager",
                    enabled = true
                },
                new ModuleEntry
                {
                    displayName = "ObjectPoolsManager",
                    typeName = "LFramework.ObjectPoolsManager",
                    enabled = true,
                    dependsOn = new[] { "LFramework.MonoManager" }
                },
                new ModuleEntry
                {
                    displayName = "UIManager",
                    typeName = "LFramework.UI.UIManager",
                    enabled = true,
                    dependsOn = new[] { "LFramework.ResourcesManager" }
                },
            };
            return manifest;
        }

        /// <summary>
        /// 按给定类型列表创建清单（编辑器扫描用 —— 调用方无需手写 typeName）。
        /// 条目按传入顺序排列；依赖关系仍需在 Inspector 中补充 dependsOn。
        /// </summary>
        public static ModuleManifest CreateFromTypes(IReadOnlyList<Type> moduleTypes)
        {
            var manifest = CreateInstance<ModuleManifest>();
            manifest.modules = new List<ModuleEntry>();
            if (moduleTypes != null)
            {
                foreach (var type in moduleTypes)
                {
                    if (type == null) continue;
                    manifest.modules.Add(new ModuleEntry
                    {
                        displayName = type.Name,
                        typeName = type.FullName,
                        enabled = true
                    });
                }
            }
            return manifest;
        }

        /// <summary>
        /// 运行时按 dependsOn 做拓扑排序后的有序列表（顺序稳定：无依赖模块保持清单顺序）。
        /// 存在环时回退为清单顺序并打印错误。
        /// </summary>
        public List<ModuleEntry> GetSortedEntries()
        {
            return TopologicalSort(modules);
        }

        // ------------------------------------------------------------------
        // 拓扑排序 (Kahn 算法)
        // ------------------------------------------------------------------

        private static List<ModuleEntry> TopologicalSort(List<ModuleEntry> entries)
        {
            if (entries == null || entries.Count == 0)
                return new List<ModuleEntry>();

            // 只保留 enabled 的条目（保持清单顺序）
            var active = entries.FindAll(e => e.enabled);
            if (active.Count == 0)
                return new List<ModuleEntry>();

            // typeName -> entry（重复 typeName 只保留第一个，后续遍历同样跳过）
            var byName = new Dictionary<string, ModuleEntry>();
            foreach (var e in active)
            {
                if (string.IsNullOrEmpty(e.typeName))
                {
                    Debug.LogError($"[ModuleManifest] 条目 \"{e.displayName}\" 的 typeName 为空，跳过。");
                    continue;
                }
                if (!byName.ContainsKey(e.typeName))
                    byName[e.typeName] = e;
                else
                    Debug.LogError($"[ModuleManifest] typeName 重复: {e.typeName}，仅保留第一个。");
            }

            // 入度
            var inDegree = new Dictionary<string, int>();
            // 依赖图: key 被 value 依赖 (key 初始化后才能初始化 value)
            var dependents = new Dictionary<string, List<string>>();

            // 按清单原始顺序遍历（重复 typeName 只计第一个），让所有下游顺序都从清单顺序推导
            foreach (var entry in active)
            {
                if (string.IsNullOrEmpty(entry.typeName)) continue; // 已在上方报错
                if (byName.TryGetValue(entry.typeName, out var canonical) && !ReferenceEquals(canonical, entry))
                    continue; // 重复 typeName 的后续条目不参与计数

                if (!inDegree.ContainsKey(entry.typeName))
                    inDegree[entry.typeName] = 0;

                if (entry.dependsOn == null) continue;

                foreach (var dep in entry.dependsOn)
                {
                    if (string.IsNullOrEmpty(dep)) continue;

                    // 依赖的模块必须存在于清单中
                    if (!byName.ContainsKey(dep))
                    {
                        Debug.LogError($"[ModuleManifest] \"{entry.displayName}\" 依赖的 \"{dep}\" 不在清单中，忽略该依赖。");
                        continue;
                    }

                    inDegree[entry.typeName]++;
                    if (!dependents.ContainsKey(dep))
                        dependents[dep] = new List<string>();
                    dependents[dep].Add(entry.typeName);
                }
            }

            // 入度为 0 的入队 —— 必须按 active（清单顺序）遍历。
            // 原实现从 Dictionary.Values 取枚举顺序（未定义），
            // 会让"拖拽调整顺序即可调整初始化优先级"只在巧合下成立（Phase 1 / P0-3 修复）
            var queue = new Queue<ModuleEntry>();
            foreach (var entry in active)
            {
                if (string.IsNullOrEmpty(entry.typeName)) continue;
                if (byName.TryGetValue(entry.typeName, out var canonical) && !ReferenceEquals(canonical, entry))
                    continue;
                if (inDegree[entry.typeName] == 0)
                    queue.Enqueue(canonical);
            }

            var sorted = new List<ModuleEntry>(byName.Count);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                sorted.Add(current);

                if (dependents.TryGetValue(current.typeName, out var deps))
                {
                    foreach (var dep in deps)
                    {
                        inDegree[dep]--;
                        if (inDegree[dep] == 0 && byName.ContainsKey(dep))
                            queue.Enqueue(byName[dep]);
                    }
                }
            }

            // 环检测
            if (sorted.Count != byName.Count)
            {
                var cyclic = new List<string>();
                foreach (var kvp in inDegree)
                {
                    if (kvp.Value > 0) cyclic.Add(kvp.Key);
                }
                Debug.LogError($"[ModuleManifest] 检测到循环依赖: {string.Join(", ", cyclic)}\n回退为清单顺序。");
                // 按清单原始顺序返回（去重），保证回退顺序也是确定的（Phase 1 / P0-3 修复）
                var fallback = new List<ModuleEntry>(byName.Count);
                var seen = new HashSet<string>();
                foreach (var entry in active)
                {
                    if (!string.IsNullOrEmpty(entry.typeName) && seen.Add(entry.typeName))
                        fallback.Add(entry);
                }
                return fallback;
            }

            return sorted;
        }
    }

    /// <summary>
    /// 模块条目 - 清单中的一行，描述一个模块的类型、依赖关系和启用状态。
    /// </summary>
    [Serializable]
    public class ModuleEntry
    {
        [Tooltip("显示名，仅用于 Inspector 可读性")]
        public string displayName = "";

        [Tooltip("完整类型名，如 LFramework.UIManager")]
        public string typeName = "";

        [Tooltip("是否启用")]
        public bool enabled = true;

        [Tooltip("依赖的模块 typeName 列表(必须在本清单中存在)。ModuleManager 会拓扑排序确保依赖先初始化。")]
        public string[] dependsOn = Array.Empty<string>();
    }
}
