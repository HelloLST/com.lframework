using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/*--------
 * 用定义的前缀 btn_自定义名称 当前以及子物体都会生成
 * 不用定义的前缀或下划线开头  忽略当前从子物体开始生成
 * 用下划线开头 忽略当前以及子物体 都不再生成
 */
public static class UIExport
{
    private static string exportString = "";
    private static Dictionary<string, string> uiTypeDic = new Dictionary<string, string>()
    {
        // 只读取以下标签的节点，不是以下标签的节点忽略自己但是会读取子节点，以_开头的忽略自己和子节点
        {"btn","Button"},   // btn_按钮名
        {"img","Image"},
        {"obj","GameObject"},
        {"text","Text"},
        {"input","InputField"},
        {"slider","Slider"},
        {"scroll","ScrollRect"},
        {"ui","View"}, // 类型为脚本，命名格式为 ui_脚本名
    };

    [MenuItem("LFramework/MagicBox/导出UI组件  %&u", false, 2)]
    private static void exportUIComp()
    {
        var prefabStage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
        if (prefabStage == null)
        {
            Debug.LogWarning("当前没有打开预设体编辑模式");
            return;
        }
        exportString = "";
        GameObject basePrebabModal = AssetDatabase.LoadAssetAtPath(prefabStage.assetPath, typeof(GameObject)) as GameObject;

        exportString += "\n#region ui component\n";
        exportUICompList(basePrebabModal.transform, "");
        exportString += "\nprivate void Reset()\n{\n";
        exportResetFunc(basePrebabModal.transform, "");
        exportString += "}\n";
        exportString += "\n#endregion\n";


        GUIUtility.systemCopyBuffer = exportString;
        Debug.Log(exportString);
        Debug.Log("已保存到剪贴板");
    }

    [MenuItem("LFramework/MagicBox/导出UI组件并写回脚本  %&i", false, 3)]
    private static void ExportUICompWriteBack()
    {
        var prefabStage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
        if (prefabStage == null)
        {
            Debug.LogWarning("当前没有打开预设体编辑模式");
            return;
        }

        GameObject prefabModal = AssetDatabase.LoadAssetAtPath(prefabStage.assetPath, typeof(GameObject)) as GameObject;
        if (prefabModal == null)
        {
            Debug.LogError("加载预制体失败");
            return;
        }

        string scriptPath = FindPanelScriptPath(prefabStage.assetPath);
        if (string.IsNullOrEmpty(scriptPath) || !File.Exists(scriptPath))
        {
            Debug.LogError($"未找到面板脚本: {scriptPath}");
            return;
        }

        // 生成字段声明块
        exportString = "";
        exportUICompList(prefabModal.transform, "");
        string fields = exportString.TrimEnd('\r', '\n');

        // 生成 Reset 方法体（仅当脚本中已有 Reset 时才替换，新面板不生成 Reset）
        exportString = "";
        exportResetFunc(prefabModal.transform, "");
        string resetBody = exportString.TrimEnd('\r', '\n');

        string source = File.ReadAllText(scriptPath, Encoding.UTF8);

        bool hasRegion = ReplaceRegionContent(ref source, "ui component", fields);
        ReplaceMethodBody(ref source, "private void Reset()", resetBody);

        if (!hasRegion)
        {
            hasRegion = InsertRegionAfterClassOpenBrace(ref source, "ui component", fields);
        }

        if (!hasRegion)
        {
            Debug.LogError("脚本中未找到 #region ui component，也未找到类声明，无法定位插入点，请手动粘贴");
            return;
        }

        File.WriteAllText(scriptPath, source, new UTF8Encoding(true));
        AssetDatabase.Refresh();
        Debug.Log($"已写回脚本: {scriptPath}");
    }

    /// <summary>
    /// 由预制体路径推导面板脚本路径，找不到则弹窗选择
    /// </summary>
    private static string FindPanelScriptPath(string prefabPath)
    {
        string prefabName = Path.GetFileNameWithoutExtension(prefabPath);
        string defaultPath = $"Assets/Scripts/UI/{prefabName}.cs";
        if (File.Exists(defaultPath)) return defaultPath;

        // 全工程搜索同名 MonoScript
        string[] guids = AssetDatabase.FindAssets($"{prefabName} t:MonoScript");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileNameWithoutExtension(path) == prefabName)
            {
                return path;
            }
        }

        string absolute = EditorUtility.OpenFilePanel("选择面板脚本", "Assets/Scripts/UI", "cs");
        if (string.IsNullOrEmpty(absolute)) return null;
        return ToAssetPath(absolute);
    }

    private static string ToAssetPath(string absolutePath)
    {
        string dataPath = Application.dataPath.Replace('\\', '/');
        absolutePath = absolutePath.Replace('\\', '/');
        if (absolutePath.StartsWith(dataPath))
        {
            return "Assets" + absolutePath.Substring(dataPath.Length);
        }
        return absolutePath;
    }

    /// <summary>
    /// 替换 source 中 #region regionName 到 #endregion regionName 之间的内容（自动对齐缩进）
    /// </summary>
    private static bool ReplaceRegionContent(ref string source, string regionName, string newContent)
    {
        string startTag = $"#region {regionName}";
        string endTag = $"#endregion {regionName}";
        int start = source.IndexOf(startTag, StringComparison.Ordinal);
        if (start < 0) return false;
        int end = source.IndexOf(endTag, start + startTag.Length, StringComparison.Ordinal);
        if (end < 0) return false;

        // #region 行的行首与缩进
        int lineStart = source.LastIndexOf('\n', start) + 1;
        int lineEnd = source.IndexOf('\n', start);
        if (lineEnd < 0) lineEnd = start + startTag.Length;
        string indent = GetLeadingWhitespace(source.Substring(lineStart, start - lineStart));

        // #endregion 行的行尾（含换行）
        int endRegionLineEnd = source.IndexOf('\n', end);
        if (endRegionLineEnd < 0) endRegionLineEnd = source.Length;
        else endRegionLineEnd++;

        string block = indent + startTag + "\n"
                     + IndentLines(newContent, indent) + "\n"
                     + indent + endTag + "\n";
        source = source.Substring(0, lineStart) + block + source.Substring(endRegionLineEnd);
        return true;
    }

    /// <summary>
    /// 替换 source 中 methodSignature 方法的方法体（仅当该方法存在时）
    /// </summary>
    private static bool ReplaceMethodBody(ref string source, string methodSignature, string newBody)
    {
        int sigIndex = source.IndexOf(methodSignature, StringComparison.Ordinal);
        if (sigIndex < 0) return false;

        int openBrace = source.IndexOf('{', sigIndex);
        if (openBrace < 0) return false;

        // 方法体缩进 = 方法签名行缩进 + 4
        int sigLineStart = source.LastIndexOf('\n', sigIndex) + 1;
        string sigIndent = GetLeadingWhitespace(source.Substring(sigLineStart, sigIndex - sigLineStart));
        string bodyIndent = sigIndent + "    ";

        // 匹配闭合大括号
        int depth = 0;
        int closeBrace = -1;
        for (int i = openBrace; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}') { depth--; if (depth == 0) { closeBrace = i; break; } }
        }
        if (closeBrace < 0) return false;

        string block = "\n" + IndentLines(newBody, bodyIndent) + "\n";
        source = source.Substring(0, openBrace + 1) + block + source.Substring(closeBrace);
        return true;
    }

    /// <summary>
    /// 在类声明大括号后插入 region 块（用于没有 region 的脚本）
    /// </summary>
    private static bool InsertRegionAfterClassOpenBrace(ref string source, string regionName, string newContent)
    {
        int classIndex = source.IndexOf("class ", StringComparison.Ordinal);
        if (classIndex < 0) return false;
        int openBrace = source.IndexOf('{', classIndex);
        if (openBrace < 0) return false;

        int lineEnd = source.IndexOf('\n', openBrace);
        if (lineEnd < 0) lineEnd = source.Length;

        int classLineStart = source.LastIndexOf('\n', classIndex) + 1;
        string classIndent = GetLeadingWhitespace(source.Substring(classLineStart, classIndex - classLineStart));
        string memberIndent = classIndent + "    ";

        string block = "\n" + memberIndent + "#region " + regionName + "\n"
                     + IndentLines(newContent, memberIndent) + "\n"
                     + memberIndent + "#endregion " + regionName + "\n";
        source = source.Substring(0, lineEnd + 1) + block + source.Substring(lineEnd + 1);
        return true;
    }

    private static string GetLeadingWhitespace(string s)
    {
        int i = 0;
        while (i < s.Length && (s[i] == ' ' || s[i] == '\t')) i++;
        return s.Substring(0, i);
    }

    private static string IndentLines(string text, string indent)
    {
        if (string.IsNullOrEmpty(text)) return text;
        string[] lines = text.Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder();
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0) sb.Append('\n');
            if (lines[i].Length > 0) sb.Append(indent);
            sb.Append(lines[i]);
        }
        return sb.ToString();
    }


    /// <summary>
    /// 导出ui组件列表文本，ui命名规范为 "{uiTypeDic中的前缀}_{任意名字}"
    /// </summary>
    private static void exportUICompList(Transform transform, string path = "")
    {
        var len = transform.childCount;
        for (var i = 0; i < len; i++)
        {
            var childTrans = transform.GetChild(i);
            var name = childTrans.name;
            var uiTypeArr = name.Split('_');
            string uiType = uiTypeArr[0];
            bool forChild = true;       // 是否递归子节点
            bool ignore = false;      // 是否忽略当前节点
            string typeName = "";
            if (!uiTypeDic.TryGetValue(uiType, out typeName))
            {
                ignore = true;
                // Debug.LogWarning("未知的ui类型:" + uiType);
            }
            // 需要特殊处理的类型
            switch (uiType)
            {
                case "ui":
                    typeName = uiTypeArr[1];
                    forChild = false;
                    break;
                case "scroll":
                    forChild = false;
                    break;
                case "":        // _name
                    ignore = true;
                    forChild = false;
                    break;
            }

            if (!ignore)
            {
                string targetStr = $"[SerializeField] private {typeName} {name};\n";
                if (exportString.IndexOf(targetStr) == -1)
                {
                    exportString += targetStr;
                }
                else
                {
                    Debug.LogError($"重复的属性名={name} path={path}");
                }

            }
            if (forChild)
            {
                exportUICompList(childTrans, path + "/" + name);
            }

        }
    }

    private static void exportResetFunc(Transform a_trans, string a_path)
    {
        var len = a_trans.childCount;
        for (var i = 0; i < len; i++)
        {
            var childTrans = a_trans.GetChild(i);
            var name = childTrans.name;
            var uiTypeArr = name.Split('_');
            string uiType = uiTypeArr[0];
            bool forChild = true;
            bool ignore = false;
            string typeName = "";
            if (!uiTypeDic.TryGetValue(uiType, out typeName))
            {
                ignore = true;
                // Debug.LogWarning("未知的ui类型:" + uiType);
            }
            switch (uiType)
            {
                case "ui":
                    typeName = uiTypeArr[1];
                    forChild = false;
                    break;
                case "scroll":
                    forChild = false;
                    break;
                case "": // 跳过
                    ignore = true;
                    forChild = false;
                    break;
            }

            if (!ignore)
            {
                string targetStr;

                if (a_path == "")
                {
                    if (uiType == "obj")
                    {
                        targetStr = $"{name} = transform.Find(\"{name}\").gameObject;\n";
                    }
                    else
                    {
                        targetStr = $"{name} = transform.Find(\"{name}\").GetComponent<{typeName}>();\n";
                    }
                }
                else
                {
                    if (uiType == "obj")
                    {
                        targetStr = $"{name} = transform.Find(\"{a_path}/{name}\").gameObject;\n";
                    }
                    else
                    {
                        targetStr = $"{name} = transform.Find(\"{a_path}/{name}\").GetComponent<{typeName}>();\n";
                    }
                }

                if (exportString.IndexOf(targetStr) == -1)
                {
                    exportString += targetStr;
                }
                else
                {
                    Debug.LogError($"重复的属性名={name} path={a_path}");
                }
            }
            if (forChild)
            {
                if (a_path == "")
                {
                    exportResetFunc(childTrans, name);
                }
                else
                {
                    exportResetFunc(childTrans, a_path + "/" + name);
                }
            }
        }
    }
}
