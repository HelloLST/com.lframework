#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace LFramework.Editor
{
    /// <summary>
    /// 一键生成 ModuleManifest SO 资产。
    /// 菜单: LFramework/Create Module Manifest Asset
    /// 生成后拖到 GameLaunch 的 _moduleManifest 字段即可。
    /// </summary>
    public static class ModuleManifestCreator
    {
        private const string MENU_PATH = "LFramework/Create Module Manifest Asset";
        private const string DEFAULT_PATH = "Assets/Resources/ModuleManifest.asset";

        [MenuItem(MENU_PATH, priority = 0)]
        public static void CreateManifest()
        {
            // 扫描当前工程所有 IModule 实现，自动生成清单（无需手写 typeName，避免拼错）
            var moduleTypes = FindAllModuleTypes();
            ModuleManifest manifest;
            if (moduleTypes.Count > 0)
            {
                manifest = ModuleManifest.CreateFromTypes(moduleTypes);
                Debug.Log($"[ModuleManifestCreator] 扫描到 {moduleTypes.Count} 个 IModule 实现：" +
                          $"{string.Join(", ", moduleTypes.Select(t => t.Name))}");
            }
            else
            {
                manifest = ModuleManifest.CreateDefault();
                Debug.LogWarning("[ModuleManifestCreator] 未扫描到任何 IModule 实现，回退为框架内置默认清单。");
            }

            // 确保目录存在
            string dir = System.IO.Path.GetDirectoryName(DEFAULT_PATH);
            if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
            {
                if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                {
                    AssetDatabase.CreateFolder("Assets", "Resources");
                }
            }

            // 如果已存在则覆盖
            var existing = AssetDatabase.LoadAssetAtPath<ModuleManifest>(DEFAULT_PATH);
            if (existing != null)
            {
                if (!EditorUtility.DisplayDialog(
                    "ModuleManifest 已存在",
                    $"路径 {DEFAULT_PATH} 已有 ModuleManifest 资产。\n点击确认覆盖为默认清单, 取消则保留现有资产。",
                    "覆盖", "取消"))
                {
                    Selection.activeObject = existing;
                    EditorGUIUtility.PingObject(existing);
                    return;
                }

                // 覆盖: 直接用新清单的 modules 列表替换
                EditorUtility.CopySerialized(manifest, existing);
                AssetDatabase.SaveAssets();
                Selection.activeObject = existing;
                EditorGUIUtility.PingObject(existing);
                Debug.Log($"[ModuleManifestCreator] 已覆盖: {DEFAULT_PATH}");
                return;
            }

            // 新建
            AssetDatabase.CreateAsset(manifest, DEFAULT_PATH);
            AssetDatabase.SaveAssets();
            Selection.activeObject = manifest;
            EditorGUIUtility.PingObject(manifest);
            Debug.Log($"[ModuleManifestCreator] 已创建: {DEFAULT_PATH}\n请将其拖到 GameLaunch 的 _moduleManifest 字段。");
        }

        private const string VALIDATE_MENU = "LFramework/Validate Module Manifest Asset";

        /// <summary>
        /// 编辑期校验清单：把"运行时才报错的 typeName 拼写/删除问题"提前到编辑期。
        /// 先在 Project 窗口选中要校验的 ModuleManifest 资产再点菜单；
        /// 未选中时回退校验默认路径 Assets/Resources/ModuleManifest.asset。
        /// </summary>
        [MenuItem(VALIDATE_MENU, priority = 1)]
        public static void ValidateManifest()
        {
            var manifest = Selection.activeObject as ModuleManifest
                           ?? AssetDatabase.LoadAssetAtPath<ModuleManifest>(DEFAULT_PATH);
            if (manifest == null)
            {
                Debug.LogWarning("[ModuleManifestCreator] 未找到要校验的 ModuleManifest（请先在 Project 窗口选中，或确认默认路径存在）。");
                return;
            }

            int total = 0, bad = 0;
            foreach (var entry in manifest.Modules)
            {
                if (!entry.enabled) continue;
                total++;

                if (string.IsNullOrEmpty(entry.typeName))
                {
                    bad++;
                    Debug.LogError($"[ModuleManifestCreator] 条目 \"{entry.displayName}\" 的 typeName 为空。");
                    continue;
                }

                if (ModuleManager.ResolveType(entry.typeName) == null)
                {
                    bad++;
                    Debug.LogError($"[ModuleManifestCreator] 无法解析类型: {entry.typeName} (条目: \"{entry.displayName}\")" +
                                   "\n运行时该条目将被跳过，请检查类名/命名空间，或该类是否已被删除/改名。");
                }
            }

            if (bad == 0)
                Debug.Log($"[ModuleManifestCreator] ✅ 校验通过：{total} 个启用条目的 typeName 全部可解析。");
            else
                Debug.LogError($"[ModuleManifestCreator] ❌ 校验失败：{bad}/{total} 个条目的 typeName 无法解析。");
        }

        /// <summary>
        /// 扫描所有已加载程序集，找出可实例化的 IModule 实现。
        /// 跳过抽象类 / 接口 / 泛型定义；不区分程序集，因此工程自定义模块会被自动发现。
        /// </summary>
        private static List<Type> FindAllModuleTypes()
        {
            var result = new List<Type>();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic) continue;

                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
                catch { continue; }

                foreach (var type in types)
                {
                    if (type == null || type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition) continue;
                    if (!typeof(IModule).IsAssignableFrom(type)) continue;
                    result.Add(type);
                }
            }

            result.Sort((a, b) => string.CompareOrdinal(a.FullName, b.FullName));
            return result;
        }
    }
}
#endif
