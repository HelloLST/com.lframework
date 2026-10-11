using UnityEngine;
using System.IO;

namespace LFramework
{
    /// <summary>
    /// 日志配置管理器 - 负责配置的持久化存储和加载
    /// </summary>
    public static class LogSettingsManager
    {
        private const string RESOURCE_PATH = "LogSettings";
        private static LogSettingsAsset _cachedAsset = null;

        /// <summary> 加载配置（从 Resources 加载 ScriptableObject） </summary>
        public static void Load()
        {
            //先尝试从 Resources 加载
            var asset = Resources.Load<LogSettingsAsset>(RESOURCE_PATH);

            if (asset != null)
            {
                asset.ApplyToCurrent();
                Debug.Log($"[LogSettings] 配置已加载: {RESOURCE_PATH}");
                Debug.Log($"[LogSettings] EnableUpload={LogUploader.EnableUpload}, UploadUrl={LogUploader.UploadUrl}");
            }
            else
            {
                Debug.Log($"[LogSettings] 配置文件不存在，使用默认配置");
            }

            _cachedAsset = asset;
        }

        /// <summary> 保存配置（保存到 ScriptableObject） </summary>
        public static void Save()
        {
            //获取或创建配置资源
            var asset = GetOrCreateAsset();
            if (asset == null)
            {
                Debug.LogError("[LogSettings] 保存配置失败：无法创建配置资源");
                return;
            }

            //从当前运行时配置更新 asset
            UpdateAssetFromCurrent(asset);

            //保存到 Resources 目录
            SaveAssetToResources(asset);

            Debug.Log($"[LogSettings] 配置已保存: {RESOURCE_PATH}");
        }

        /// <summary> 获取或创建配置资源 </summary>
        private static LogSettingsAsset GetOrCreateAsset()
        {
            //先尝试从 Resources 加载
            var asset = Resources.Load<LogSettingsAsset>(RESOURCE_PATH);

            if (asset == null)
            {
                //创建新资源
                asset = LogSettingsAsset.CreateInstance<LogSettingsAsset>();
                Debug.Log("[LogSettings] 创建新的配置资源");
            }

            return asset;
        }

        /// <summary> 从当前运行时配置更新 asset </summary>
        private static void UpdateAssetFromCurrent(LogSettingsAsset asset)
        {
            //日志级别
            asset.MinLevel = LogManager.MinLevel;

            //输出配置
            asset.EnableConsole = LogManager.EnableConsole;
            asset.EnableFile = LogManager.EnableFile;
            asset.EnableInEditor = LogManager.EnableInEditor;

            //刷新模式
            asset.FlushMode = LogManager.FlushMode;
            asset.BufferCountThreshold = LogManager.BufferCountThreshold;
            asset.BufferTimeThreshold = LogManager.BufferTimeThreshold;

            //文件配置
            asset.SaveDays = LogManager.SaveDays;
            asset.MaxFileSizeMB = LogManager.MaxFileSizeBytes / 1024 / 1024;

            //其他配置
            asset.EnableSanitization = LogManager.EnableSanitization;
            asset.RecordStackTrace = LogManager.RecordStackTrace;
            asset.EnableExceptionCatcher = LogManager.EnableExceptionCatcher;

            //远程上传
            asset.EnableUpload = LogUploader.EnableUpload;
            asset.ServerType = LogUploader.Type;
            asset.UploadUrl = LogUploader.UploadUrl;
            asset.Username = LogUploader.Username;
            asset.Password = LogUploader.Password;
            asset.UploadLevel = LogUploader.UploadLevel;
            asset.ConnectionTimeout = LogUploader.ConnectionTimeout;
        }

        /// <summary> 保存 asset 到 Resources 目录 </summary>
        private static void SaveAssetToResources(LogSettingsAsset asset)
        {
#if UNITY_EDITOR
            //在编辑器中，保存到 Assets/Resources 目录下
            string resourcesPath = Path.Combine(Application.dataPath, "Resources");

            if (!Directory.Exists(resourcesPath))
            {
                Directory.CreateDirectory(resourcesPath);
            }

            string assetPath = Path.Combine(resourcesPath, $"{RESOURCE_PATH}.asset");

            //检查文件是否已存在
            if (File.Exists(assetPath))
            {
                //已存在则直接保存（更新）
                UnityEditor.AssetDatabase.SaveAssets();
                UnityEditor.EditorUtility.SetDirty(asset);
                UnityEditor.AssetDatabase.SaveAssets();
                Debug.Log($"[LogSettings] 配置资源已更新: {assetPath}");
            }
            else
            {
                //不存在则创建新资源
                UnityEditor.AssetDatabase.CreateAsset(asset, $"Assets/Resources/{RESOURCE_PATH}.asset");
                Debug.Log($"[LogSettings] 配置资源已创建: {assetPath}");
            }

            UnityEditor.AssetDatabase.Refresh();
#endif
        }

        /// <summary> 配置文件是否存在 </summary>
        public static bool Exists()
        {
            var asset = Resources.Load<LogSettingsAsset>(RESOURCE_PATH);
            return asset != null;
        }

        /// <summary> 获取当前配置资源（用于编辑器窗口显示） </summary>
        public static LogSettingsAsset GetCurrentAsset()
        {
            if (_cachedAsset != null)
                return _cachedAsset;

            _cachedAsset = Resources.Load<LogSettingsAsset>(RESOURCE_PATH);
            return _cachedAsset;
        }

        /// <summary> 重新加载配置（清除缓存） </summary>
        public static void Reload()
        {
            _cachedAsset = null;
            Load();
        }
    }
}