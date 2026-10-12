using UnityEngine;

namespace LFramework.UI
{
    /// <summary>
    /// UI 系统路径配置。
    ///
    /// 框架内置默认值开箱可用；换工程若目录结构不同，只需在 Resources 下放一份名为
    /// "UISettings" 的资产并改字段，**不需要改框架代码**。
    /// 资产缺失时自动回退到默认值，绝不空引用。
    /// </summary>
    [CreateAssetMenu(menuName = "LFramework/UI Settings", fileName = "UISettings")]
    public class UISettings : ScriptableObject
    {
        [Tooltip("UI 根预制体在 Resources 下的路径（不含扩展名）")]
        public string rootPrefabPath = DefaultRootPrefabPath;

        [Tooltip("面板预制体目录（不含扩展名）。面板实际路径 = 该目录 + \"/\" + 面板类型名")]
        public string panelPrefabDir = DefaultPanelPrefabDir;

        [Tooltip("各层级在 UIRoot 下的子节点名，顺序须与 UIPanelLayer 枚举一致。留空或长度不足时回退为枚举名")]
        public string[] layerNodeNames = System.Array.Empty<string>();

        public const string DefaultRootPrefabPath = "Prefabs/UI/UIRoot";
        public const string DefaultPanelPrefabDir = "Prefabs/UI/UIPanel";

        /// <summary>缓存的配置资产（可能为 null，此时全部走默认值）。
        /// 缓存由 ScriptableConfigCache 统一管理：关闭 Domain Reload 时进 Play 自动重置（Phase 5 / 5.5）。</summary>
        public static UISettings Instance => ScriptableConfigCache<UISettings>.Get("UISettings");

        /// <summary>UI 根预制体路径（带默认值兜底）。</summary>
        public static string RootPrefabPath =>
            Instance != null && !string.IsNullOrEmpty(Instance.rootPrefabPath)
                ? Instance.rootPrefabPath
                : DefaultRootPrefabPath;

        /// <summary>面板预制体目录（带默认值兜底）。</summary>
        public static string PanelPrefabDir =>
            Instance != null && !string.IsNullOrEmpty(Instance.panelPrefabDir)
                ? Instance.panelPrefabDir
                : DefaultPanelPrefabDir;

        /// <summary>按面板类型名拼出预制体路径。</summary>
        public static string GetPanelPath(System.Type panelType) =>
            $"{PanelPrefabDir}/{panelType.Name}";

        /// <summary>取层级在 UIRoot 下的子节点名；未配置时回退为枚举名。</summary>
        public static string GetLayerNodeName(UIPanelLayer layer)
        {
            string[] names = Instance != null ? Instance.layerNodeNames : null;
            int index = (int)layer;
            if (names != null && index >= 0 && index < names.Length && !string.IsNullOrEmpty(names[index]))
            {
                return names[index];
            }
            return layer.ToString();
        }
    }
}
