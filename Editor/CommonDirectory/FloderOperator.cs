using System.IO;
using UnityEditor;
using UnityEngine;
using System;
using System.Diagnostics;

public static class FloderOperator
{
    [InitializeOnLoadMethod]
    private static void Initialize()
    {
        EditorApplication.projectWindowItemOnGUI += CheckForMiddleClick;
    }

    private static void CheckForMiddleClick(string guid, Rect selectionRect)
    {
        if (Event.current.type == EventType.MouseDown
             && Event.current.button == 2
             && selectionRect.Contains(Event.current.mousePosition))
        {
            string strPath = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetExtension(strPath) == string.Empty)//是文件夹
            {
                System.Diagnostics.Process.Start(Path.GetFullPath(strPath));
            }
            else //文件
            {
                System.Diagnostics.Process.Start("explorer.exe", "/select," + Path.GetFullPath(strPath));
            }
        }
    }

    #region 查看路径菜单

    [MenuItem("LFramework/文件夹管理/查看路径/打开Assets目录")]
    public static void OpenAssetsFolder()
    {
        OpenFolder(Application.dataPath);
    }

    [MenuItem("LFramework/文件夹管理/查看路径/打开PersistentDataPath目录")]
    private static void OpenPersistentDataFolder()
    {
        string path = Application.persistentDataPath;

        if (Directory.Exists(path))
        {
            Application.OpenURL(path);
        }
        else
        {
            UnityEngine.Debug.LogError("The persistent data path does not exist: " + path);
        }
    }

    [MenuItem("LFramework/文件夹管理/查看路径/打开StreamingAssets目录")]
    public static void OpenStreamingAssetsFolder()
    {
        string path = Application.streamingAssetsPath;
        if (Directory.Exists(path))
        {
            OpenFolder(path);
        }
        else
        {
            EditorUtility.DisplayDialog("提示", "StreamingAssets文件夹不存在，请先创建！", "确定");
        }
    }

    #endregion 查看路径菜单

    #region 创建路径菜单

    [MenuItem("LFramework/文件夹管理/创建路径/创建StreamingAssets文件夹")]
    public static void CreateStreamingAssetsFolder()
    {
        CreateFolder(Application.streamingAssetsPath, "StreamingAssets");
    }

    [MenuItem("LFramework/文件夹管理/创建路径/创建Resources文件夹")]
    public static void CreateResourcesFolder()
    {
        CreateFolder(Application.dataPath + "/Resources", "Resources");
    }

    [MenuItem("LFramework/文件夹管理/创建路径/创建Editor文件夹")]
    public static void CreateEditorFolder()
    {
        CreateFolder(Application.dataPath + "/Editor", "Editor");
    }

    [MenuItem("LFramework/文件夹管理/创建路径/创建Plugins文件夹")]
    public static void CreatePluginsFolder()
    {
        CreateFolder(Application.dataPath + "/Plugins", "Plugins");
    }

    [MenuItem("LFramework/文件夹管理/创建路径/创建Scripts文件夹")]
    public static void CreateScriptsFolder()
    {
        CreateFolder(Application.dataPath + "/Scripts", "Scripts");
    }

    [MenuItem("LFramework/文件夹管理/创建路径/创建Prefabs文件夹")]
    public static void CreatePrefabsFolder()
    {
        CreateFolder(Application.dataPath + "/Prefabs", "Prefabs");
    }

    [MenuItem("LFramework/文件夹管理/创建路径/创建Materials文件夹")]
    public static void CreateMaterialsFolder()
    {
        CreateFolder(Application.dataPath + "/Materials", "Materials");
    }

    [MenuItem("LFramework/文件夹管理/创建路径/创建Textures文件夹")]
    public static void CreateTexturesFolder()
    {
        CreateFolder(Application.dataPath + "/Textures", "Textures");
    }

    [MenuItem("LFramework/文件夹管理/创建路径/创建Audio文件夹")]
    public static void CreateAudioFolder()
    {
        CreateFolder(Application.dataPath + "/Audio", "Audio");
    }

    [MenuItem("LFramework/文件夹管理/创建路径/创建Animations文件夹")]
    public static void CreateAnimationsFolder()
    {
        CreateFolder(Application.dataPath + "/Animations", "Animations");
    }

    [MenuItem("LFramework/文件夹管理/创建路径/创建常用文件夹结构")]
    public static void CreateCommonFolderStructure()
    {
        string[] folders = {
                "Scripts",
                "Prefabs",
                "Materials",
                "Textures",
                "Audio",
                "Animations",
                "Scenes",
                "Resources",
                "StreamingAssets"
            };

        int createdCount = 0;
        foreach (string folder in folders)
        {
            string folderPath = Application.dataPath + "/" + folder;
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
                createdCount++;
            }
        }

        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("创建完成",
            $"常用文件夹结构创建完成！\n新创建了 {createdCount} 个文件夹", "确定");
    }

    #endregion 创建路径菜单

    #region 辅助方法

    /// <summary>
    /// 打开指定文件夹
    /// </summary>
    /// <param name="path">文件夹路径</param>
    private static void OpenFolder(string path)
    {
        if (Directory.Exists(path))
        {
            path = path.Replace("/", "\\");
            Process.Start("explorer.exe", path);
        }
        else
        {
            EditorUtility.DisplayDialog("错误", $"路径不存在：{path}", "确定");
        }
    }

    /// <summary>
    /// 创建文件夹
    /// </summary>
    /// <param name="path">文件夹路径</param>
    /// <param name="folderName">文件夹名称（用于显示）</param>
    private static void CreateFolder(string path, string folderName)
    {
        if (Directory.Exists(path))
        {
            EditorUtility.DisplayDialog("提示", $"{folderName} 文件夹已存在！", "确定");
            return;
        }

        try
        {
            Directory.CreateDirectory(path);
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("成功", $"{folderName} 文件夹创建成功！", "确定");
        }
        catch (System.Exception e)
        {
            EditorUtility.DisplayDialog("错误", $"创建 {folderName} 文件夹失败：{e.Message}", "确定");
        }
    }

    #endregion 辅助方法
}