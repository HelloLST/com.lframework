using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using LFramework.UI;

namespace LFramework.Editor
{
    /// <summary>
    /// 面板创建向导：一键生成面板脚本 + 预制体到框架约定目录
    /// 脚本 -> 业务程序集目录（分层后为 Assets/Scripts/Game/UI；换工程时改此常量）
    /// 预制体 -> Assets/Resources/Prefabs/UI/UIPanel/{Name}.prefab
    /// </summary>
    public class PanelCreatorWindow : EditorWindow
    {
        // 分层重构后业务脚本统一归在 Game 程序集下。
        // 若仍生成到 Assets/Scripts/UI，脚本会落进默认 Assembly-CSharp，
        // 而 Assembly-CSharp 不引用 LFramework → 生成即编译失败。
        private const string ScriptDir = "Assets/Scripts/Game/UI";
        private const string PrefabDir = "Assets/Resources/Prefabs/UI/UIPanel";

        private string panelName = "UIXXXPanel";
        private string namespaceName = "LFramework.UI";
        private bool genericBase;
        private string dataClassName = "PanelData";
        private UIPanelLayer layer = UIPanelLayer.Normal;
        private bool generateScript = true;
        private bool generatePrefab = true;

        [MenuItem("LFramework/MagicBox/创建UIPanel面板  %&k", false, 1)]
        private static void OpenWindow()
        {
            var window = GetWindow<PanelCreatorWindow>(true, "创建UIPanel面板");
            window.minSize = new Vector2(400, 340);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("面板创建向导", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            panelName = EditorGUILayout.TextField("面板类名", panelName);
            namespaceName = EditorGUILayout.TextField("命名空间", namespaceName);
            genericBase = EditorGUILayout.Toggle("基类 UIPanelBase<TData>", genericBase);
            if (genericBase)
            {
                dataClassName = EditorGUILayout.TextField("数据类名", dataClassName);
            }
            layer = (UIPanelLayer)EditorGUILayout.EnumPopup("所属层", layer);
            generateScript = EditorGUILayout.Toggle("生成脚本", generateScript);
            generatePrefab = EditorGUILayout.Toggle("生成预制体", generatePrefab);

            EditorGUILayout.Space();
            GUI.enabled = generateScript || generatePrefab;
            if (GUILayout.Button("创建", GUILayout.Height(32)))
            {
                Generate();
            }
            GUI.enabled = true;

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("命名约定: 控件字段用 btn_/img_/text_ 等前缀，基类 Awake 自动绑定，无需手写 Reset()。", MessageType.Info);
        }

        private void Generate()
        {
            if (!IsValidIdentifier(panelName))
            {
                EditorUtility.DisplayDialog("错误", "面板类名不是合法的 C# 标识符", "确定");
                return;
            }
            if (!IsValidNamespace(namespaceName))
            {
                EditorUtility.DisplayDialog("错误", "命名空间不合法", "确定");
                return;
            }
            if (genericBase && !IsValidIdentifier(dataClassName))
            {
                EditorUtility.DisplayDialog("错误", "数据类名不是合法的 C# 标识符", "确定");
                return;
            }

            string scriptPath = $"{ScriptDir}/{panelName}.cs";
            string prefabPath = $"{PrefabDir}/{panelName}.prefab";

            if (generateScript && File.Exists(scriptPath))
            {
                EditorUtility.DisplayDialog("错误", $"脚本已存在: {scriptPath}", "确定");
                return;
            }
            if (generatePrefab && File.Exists(prefabPath))
            {
                EditorUtility.DisplayDialog("错误", $"预制体已存在: {prefabPath}", "确定");
                return;
            }

            if (generateScript)
            {
                Directory.CreateDirectory(ScriptDir);
                string content = BuildScriptContent();
                File.WriteAllText(scriptPath, content, new UTF8Encoding(true));
                AssetDatabase.Refresh();
                Debug.Log($"已生成面板脚本: {scriptPath}");
            }

            if (generatePrefab)
            {
                if (generateScript)
                {
                    // 新脚本需等编译完成后才能 AddComponent
                    WaitUntilCompiled(() => CreatePanelPrefab(prefabPath));
                }
                else
                {
                    CreatePanelPrefab(prefabPath);
                }
            }
            else if (generateScript)
            {
                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(scriptPath);
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
            }

            Close();
        }

        private void CreatePanelPrefab(string prefabPath)
        {
            if (PrefabStageUtility.GetCurrentPrefabStage() != null)
            {
                EditorUtility.DisplayDialog("提示", "请先退出预制体编辑模式，再执行创建。", "确定");
                return;
            }

            Type panelType = FindPanelType(namespaceName, panelName);
            if (panelType == null)
            {
                EditorUtility.DisplayDialog("错误",
                    $"未找到面板类型 {namespaceName}.{panelName}，预制体未生成。\n请检查脚本编译是否成功。", "确定");
                return;
            }

            if (File.Exists(prefabPath))
            {
                EditorUtility.DisplayDialog("错误", $"预制体已存在: {prefabPath}", "确定");
                return;
            }

            Directory.CreateDirectory(PrefabDir);

            var root = new GameObject(panelName, typeof(RectTransform));
            RectTransform rt = root.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var bg = root.AddComponent<Image>();
            bg.color = new Color(0, 0, 0, 0.45f);

            root.AddComponent<CanvasGroup>();
            root.AddComponent(panelType);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            UnityEngine.Object.DestroyImmediate(root);

            if (prefab == null)
            {
                EditorUtility.DisplayDialog("错误", $"预制体保存失败: {prefabPath}", "确定");
                return;
            }

            Debug.Log($"已生成面板预制体: {prefabPath}");
            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);
            AssetDatabase.OpenAsset(prefab);
        }

        private string BuildScriptContent()
        {
            string layerName = layer.ToString();
            var sb = new StringBuilder();
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine("using UnityEngine.UI;");
            sb.AppendLine("using Cysharp.Threading.Tasks;");
            sb.AppendLine("using LFramework.UI;");
            sb.AppendLine();
            sb.AppendLine($"namespace {namespaceName}");
            sb.AppendLine("{");
            sb.AppendLine($"    [UILayer(UIPanelLayer.{layerName})]");
            if (genericBase)
            {
                sb.AppendLine($"    public class {panelName} : UIPanelBase<{dataClassName}>");
            }
            else
            {
                sb.AppendLine($"    public class {panelName} : UIPanelBase");
            }
            sb.AppendLine("    {");
            sb.AppendLine("        #region ui component");
            sb.AppendLine();
            sb.AppendLine("        // 控件字段命名约定: btn_/img_/text_ 等前缀，基类 Awake 自动绑定");
            sb.AppendLine("        // 例如: [SerializeField] private Button btn_Close;");
            sb.AppendLine();
            sb.AppendLine("        #endregion ui component");
            sb.AppendLine();
            sb.AppendLine("        public override void OnUIAwake()");
            sb.AppendLine("        {");
            sb.AppendLine("            base.OnUIAwake();");
            sb.AppendLine("        }");
            if (genericBase)
            {
                sb.AppendLine();
                sb.AppendLine($"        public override void InitPanelData({dataClassName} data)");
                sb.AppendLine("        {");
                sb.AppendLine("            base.InitPanelData(data);");
                sb.AppendLine("        }");
            }
            sb.AppendLine("    }");
            if (genericBase)
            {
                sb.AppendLine();
                sb.AppendLine("    [System.Serializable]");
                sb.AppendLine($"    public class {dataClassName}");
                sb.AppendLine("    {");
                sb.AppendLine("    }");
            }
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static void WaitUntilCompiled(Action action)
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += () => WaitUntilCompiled(action);
            }
            else
            {
                action();
            }
        }

        private static Type FindPanelType(string ns, string typeName)
        {
            string fullName = string.IsNullOrEmpty(ns) ? typeName : $"{ns}.{typeName}";
            foreach (System.Reflection.Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = asm.GetType(fullName, false);
                if (t != null && typeof(UIPanelBase).IsAssignableFrom(t)) return t;
            }
            foreach (System.Reflection.Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types; }
                for (int i = 0; i < types.Length; i++)
                {
                    Type t = types[i];
                    if (t != null && t.Name == typeName && typeof(UIPanelBase).IsAssignableFrom(t))
                    {
                        return t;
                    }
                }
            }
            return null;
        }

        private static bool IsValidIdentifier(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (!(char.IsLetter(name[0]) || name[0] == '_')) return false;
            for (int i = 1; i < name.Length; i++)
            {
                if (!(char.IsLetterOrDigit(name[i]) || name[i] == '_')) return false;
            }
            return true;
        }

        private static bool IsValidNamespace(string ns)
        {
            if (string.IsNullOrEmpty(ns)) return false;
            string[] parts = ns.Split('.');
            for (int i = 0; i < parts.Length; i++)
            {
                if (!IsValidIdentifier(parts[i])) return false;
            }
            return true;
        }
    }
}
