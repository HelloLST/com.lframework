using UnityEngine;
using UnityEditor;
using System.IO;

namespace LFramework
{
    /// <summary>
    /// 日志系统配置窗口
    /// </summary>
    public class LogSettingsWindow : EditorWindow
    {
        private static LogSettingsWindow _window;
        private Vector2 _scrollPosition;
        private LogSettingsAsset _settings;

        [MenuItem("LFramework/日志设置", false, 100)]
        public static void Open()
        {
            _window = GetWindow<LogSettingsWindow>("日志设置", true);
            _window.minSize = new Vector2(450, 650);
            _window.Show();

            //打开窗口时加载配置
            _window.LoadSettings();
        }

        /// <summary> 加载配置 </summary>
        private void LoadSettings()
        {
            //从 Resources 加载配置资源
            _settings = Resources.Load<LogSettingsAsset>("LogSettings");

            if (_settings == null)
            {
                //如果不存在，创建一个临时对象用于编辑
                _settings = ScriptableObject.CreateInstance<LogSettingsAsset>();
                Debug.Log("[LogSettingsWindow] 未找到配置文件，使用默认配置");
            }

            //将配置应用到运行时（内存中的配置）
            _settings.ApplyToCurrent();
        }

        /// <summary> 保存配置 </summary>
        private void SaveSettings()
        {
            //从当前运行时配置更新 _settings
            UpdateSettingsFromRuntime();

            //保存到 Resources 目录
            SaveAssetToResources(_settings);

            EditorUtility.DisplayDialog("保存配置", "日志配置已保存！", "确定");
        }

        /// <summary> 从运行时配置更新 _settings </summary>
        private void UpdateSettingsFromRuntime()
        {
            //日志级别
            _settings.MinLevel = LogManager.MinLevel;

            //输出配置
            _settings.EnableConsole = LogManager.EnableConsole;
            _settings.EnableFile = LogManager.EnableFile;
            _settings.EnableInEditor = LogManager.EnableInEditor;

            //刷新模式
            _settings.FlushMode = LogManager.FlushMode;
            _settings.BufferCountThreshold = LogManager.BufferCountThreshold;
            _settings.BufferTimeThreshold = LogManager.BufferTimeThreshold;

            //文件配置
            _settings.SaveDays = LogManager.SaveDays;
            _settings.MaxFileSizeMB = LogManager.MaxFileSizeBytes / 1024 / 1024;

            //其他配置
            _settings.EnableSanitization = LogManager.EnableSanitization;
            _settings.RecordStackTrace = LogManager.RecordStackTrace;
            _settings.EnableExceptionCatcher = LogManager.EnableExceptionCatcher;

            //远程上传
            _settings.EnableUpload = LogUploader.EnableUpload;
            _settings.ServerType = LogUploader.Type;
            _settings.UploadUrl = LogUploader.UploadUrl;
            _settings.Username = LogUploader.Username;
            _settings.Password = LogUploader.Password;
            _settings.UploadLevel = LogUploader.UploadLevel;
            _settings.ConnectionTimeout = LogUploader.ConnectionTimeout;
        }

        /// <summary> 保存到 Resources 目录 </summary>
        private void SaveAssetToResources(LogSettingsAsset asset)
        {
            string resourcesPath = Path.Combine(Application.dataPath, "Resources");

            if (!Directory.Exists(resourcesPath))
            {
                Directory.CreateDirectory(resourcesPath);
            }

            string assetPath = $"Assets/Resources/LogSettings.asset";

            //检查是否已存在
            var existingAsset = AssetDatabase.LoadAssetAtPath<LogSettingsAsset>(assetPath);
            if (existingAsset != null)
            {
                //更新现有资源
                EditorUtility.CopySerialized(asset, existingAsset);
                AssetDatabase.SaveAssets();
            }
            else
            {
                //创建新资源
                AssetDatabase.CreateAsset(asset, assetPath);
                AssetDatabase.SaveAssets();
            }

            AssetDatabase.Refresh();
            Debug.Log($"[LogSettingsWindow] 配置已保存到: {assetPath}");
        }

        private void OnGUI()
        {
            //将 UI 值应用到运行时配置（实时生效）
            ApplyUIToRuntime();

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("日志系统配置", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            // ==================== 日志级别配置 ====================
            EditorGUILayout.LabelField("【日志级别】", EditorStyles.boldLabel);
            _settings.MinLevel = (LogLevel)EditorGUILayout.EnumPopup("最小日志级别:", _settings.MinLevel);
            EditorGUILayout.HelpBox("只输出大于等于该级别的日志。Debug输出所有日志，Fatal只输出致命错误。", MessageType.Info);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("【输出配置】", EditorStyles.boldLabel);
            _settings.EnableConsole = EditorGUILayout.Toggle("输出到控制台:", _settings.EnableConsole);
            _settings.EnableFile = EditorGUILayout.Toggle("输出到文件:", _settings.EnableFile);
            _settings.EnableInEditor = EditorGUILayout.Toggle("编辑器下启用文件日志:", _settings.EnableInEditor);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("【刷新模式】", EditorStyles.boldLabel);
            _settings.FlushMode = (LogFlushMode)EditorGUILayout.EnumPopup("刷新模式:", _settings.FlushMode);

            if (_settings.FlushMode == LogFlushMode.DoubleBuffer)
            {
                _settings.BufferCountThreshold = EditorGUILayout.IntSlider("缓冲数量阈值:", _settings.BufferCountThreshold, 1, 100);
                _settings.BufferTimeThreshold = EditorGUILayout.Slider("缓冲时间阈值(秒):", _settings.BufferTimeThreshold, 0.5f, 30f);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("【文件配置】", EditorStyles.boldLabel);
            _settings.SaveDays = EditorGUILayout.IntSlider("日志保留天数:", _settings.SaveDays, 1, 30);
            _settings.MaxFileSizeMB = (int)EditorGUILayout.Slider("单个文件最大大小(MB):", _settings.MaxFileSizeMB, 1, 100);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("【其他配置】", EditorStyles.boldLabel);
            _settings.EnableSanitization = EditorGUILayout.Toggle("启用敏感信息脱敏:", _settings.EnableSanitization);
            _settings.RecordStackTrace = EditorGUILayout.Toggle("记录堆栈跟踪:", _settings.RecordStackTrace);
            _settings.EnableExceptionCatcher = EditorGUILayout.Toggle("启用全局异常捕获:", _settings.EnableExceptionCatcher);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("【远程上传配置】", EditorStyles.boldLabel);
            _settings.EnableUpload = EditorGUILayout.Toggle("启用远程上传:", _settings.EnableUpload);

            if (_settings.EnableUpload)
            {
                EditorGUILayout.BeginVertical("box");
                _settings.ServerType = (LogUploader.ServerType)EditorGUILayout.EnumPopup("服务器类型:", _settings.ServerType);
                _settings.UploadUrl = EditorGUILayout.TextField("服务器地址:", _settings.UploadUrl);

                if (_settings.ServerType == LogUploader.ServerType.WebDAV)
                {
                    EditorGUILayout.HelpBox("WebDAV 需要提供用户名和密码进行 Basic Auth 认证", MessageType.Info);
                    _settings.Username = EditorGUILayout.TextField("用户名:", _settings.Username);
                    _settings.Password = EditorGUILayout.PasswordField("密码:", _settings.Password);
                }

                _settings.UploadLevel = (LogLevel)EditorGUILayout.EnumPopup("上传日志级别:", _settings.UploadLevel);
                _settings.ConnectionTimeout = EditorGUILayout.IntSlider("连接超时(秒):", _settings.ConnectionTimeout, 3, 30);

                EditorGUILayout.Space();

                //连接测试按钮和状态显示
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("测试连接"))
                {
                    bool connected = LogUploader.TestConnectionSync();
                    if (connected)
                    {
                        EditorUtility.DisplayDialog("连接测试", "服务器连接成功！", "确定");
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("连接测试", "服务器连接失败！\n请检查服务器地址和配置是否正确。", "确定");
                    }
                }

                if (GUILayout.Button("强制重试上传"))
                {
                    LogUploader.ForceRetry();
                    EditorUtility.DisplayDialog("重试上传", "已触发重试，请稍后查看待上传数量。", "确定");
                }
                EditorGUILayout.EndHorizontal();

                //显示连接状态
                bool isConnected = LogUploader.CheckConnection();
                string statusColor = isConnected ? "green" : "red";
                string statusText = isConnected ? "已连接" : "未连接";
                EditorGUILayout.LabelField($"连接状态: <color={statusColor}>{statusText}</color>");
                EditorGUILayout.LabelField($"待上传日志: {LogUploader.GetPendingCount()} 条");

                EditorGUILayout.HelpBox($"只上传 {_settings.UploadLevel} 及以上级别的日志", MessageType.Info);
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndScrollView();

            // ==================== 保存按钮 ====================
            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("保存配置"))
            {
                SaveSettings();
            }

            if (GUILayout.Button("重置为默认"))
            {
                if (EditorUtility.DisplayDialog("重置配置", "确定要重置所有配置为默认值吗？", "确定", "取消"))
                {
                    //创建新的默认配置
                    _settings = ScriptableObject.CreateInstance<LogSettingsAsset>();
                    ApplyUIToRuntime();
                    SaveSettings();
                }
            }
            EditorGUILayout.EndHorizontal();

            // 配置文件状态
            bool configExists = LogSettingsManager.Exists();
            string configStatus = configExists ? "✓ 配置文件已存在" : "✗ 尚未保存配置";
            EditorGUILayout.LabelField($"配置状态: {configStatus}");

            // 底部提示
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("提示: 修改后实时生效。保存配置后，打包游戏启动时会自动加载。", EditorStyles.helpBox);
        }

        /// <summary> 将 UI 值应用到运行时配置 </summary>
        private void ApplyUIToRuntime()
        {
            if (_settings == null) return;
            // 走统一入口，避免直接写静态字段
            _settings.ApplyToCurrent();
        }

        /// <summary> 窗口关闭时自动保存 </summary>
        private void OnDestroy()
        {
            if (_settings != null)
            {
                //窗口关闭时自动保存
                ApplyUIToRuntime();
                SaveSettings();
            }
        }
    }
}