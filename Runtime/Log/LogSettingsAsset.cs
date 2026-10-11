using UnityEngine;

namespace LFramework
{
    /// <summary>
    /// 日志系统配置（ScriptableObject）
    /// 用于持久化存储，存放在 Resources 目录下
    /// </summary>
    public class LogSettingsAsset : ScriptableObject
    {
        //日志级别配置
        [Header("日志级别")]
        public LogLevel MinLevel = LogLevel.Debug;

        //输出配置
        [Header("输出配置")]
        public bool EnableConsole = true;
        public bool EnableFile = true;
        public bool EnableInEditor = true;

        //刷新模式配置
        [Header("刷新模式")]
        public LogFlushMode FlushMode = LogFlushMode.DoubleBuffer;
        public int BufferCountThreshold = 10;
        public float BufferTimeThreshold = 3f;

        //文件配置
        [Header("文件配置")]
        public int SaveDays = 7;
        public int MaxFileSizeMB = 10;

        //其他配置
        [Header("其他配置")]
        public bool EnableSanitization = true;
        public bool RecordStackTrace = true;
        public bool EnableExceptionCatcher = true;

        //远程上传配置
        [Header("远程上传配置")]
        public bool EnableUpload = false;
        public LogUploader.ServerType ServerType = LogUploader.ServerType.HTTP;
        public string UploadUrl = "";
        public string Username = "";
        public string Password = "";
        public LogLevel UploadLevel = LogLevel.Fatal;
        public int ConnectionTimeout = 5;

        /// <summary> 从当前运行时配置创建数据 </summary>
        public static LogSettingsAsset CreateFromCurrent()
        {
            var asset = CreateInstance<LogSettingsAsset>();

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

            return asset;
        }

        /// <summary> 应用到当前运行时配置 </summary>
        public void ApplyToCurrent()
        {
            LogManager.ApplySettings(this);
            LogUploader.ApplySettings(this);
        }
    }
}