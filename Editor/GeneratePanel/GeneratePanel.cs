using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System.IO;

#if UNITY_EDITOR
public class GeneratePanel : Editor
{
    [MenuItem("LFramework/MagicBox/创建场景脚本--待修改 %&s", false, 4)]
    private static void GenerateSceneCS()
    {
        var objs = Selection.gameObjects;
        var selected = Selection.assetGUIDs;
        for (int i = 0; i < selected.Length; i++)
        {
            string objPath = AssetDatabase.GUIDToAssetPath(selected[i]);

            if (!objPath.EndsWith(".unity"))
                continue;

            objPath = objPath.Replace(".unity", "");
            int index = objPath.LastIndexOf('/');
            string name = objPath;
            if (index > 0)
                name = objPath.Substring(index + 1);

            string filePath = $"Assets/Scripts/Scene/{name}Scene.cs";

            if (!File.Exists(filePath))
            {
                string content =
                #region cs code
$@"using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LFramework;

public class {name}Scene : SceneState
{{
    /// <summary>
    ///
    /// </summary>
    public {name}Scene()
    {{
        sceneName = ""{name}"";
    }}

    public override void OnEnter()
    {{
        // panelManager.Push(new {name}Panel());
    }}
}}
";
                #endregion
                File.WriteAllText(filePath, content);
                Debug.Log($"生成完毕\n路径为{filePath}");
                AssetDatabase.Refresh();
            }
            else
                Debug.Log("已存在该文件");
        }
    }
}
#endif
