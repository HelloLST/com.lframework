using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace LFramework
{
    /// <summary>
    /// 日志级别
    /// </summary>
    public enum LogLevel
    {
        Debug = 0,
        Info = 1,
        Warning = 2,
        Error = 3,
        Fatal = 4
    }

    /// <summary>
    /// 日志刷新模式
    /// </summary>
    public enum LogFlushMode
    {
        DoubleBuffer = 0,
        RealTime = 1
    }

    /// <summary>
    /// 日志条目
    /// </summary>
    [Serializable]
    public class LogEntry
    {
        // 设备标识 / 版本号不随日志变化，静态缓存避免每条日志都查询
        private static readonly string _deviceId = SystemInfo.deviceUniqueIdentifier;
        private static readonly string _gameVersion = Application.version;

        public LogLevel Level { get; set; }
        public string Message { get; set; }
        public string StackTrace { get; set; }
        public DateTime Time { get; set; }
        public string Tag { get; set; }
        public string DeviceId { get; set; }
        public string GameVersion { get; set; }
        public string SceneName { get; set; }

        public LogEntry(LogLevel level, string message, string stackTrace = "", string tag = "")
        {
            Level = level;
            Message = message;
            StackTrace = stackTrace;
            Time = DateTime.Now;
            Tag = tag;
            DeviceId = _deviceId;
            GameVersion = _gameVersion;
            try { SceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name; }
            catch { SceneName = "Unknown"; }
        }

        public string Format()
        {
            string levelStr = Level.ToString().ToUpper();
            string timeStr = Time.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string tagStr = string.IsNullOrEmpty(Tag) ? "" : $"[{Tag}] ";
            return $"[{timeStr}][{levelStr}] {tagStr}{Message}\n{StackTrace}";
        }

        public string FormatWithDevice()
        {
            string levelStr = Level.ToString().ToUpper();
            string timeStr = Time.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string tagStr = string.IsNullOrEmpty(Tag) ? "" : $"[{Tag}] ";
            string deviceInfo = string.IsNullOrEmpty(SceneName) ? "" : $"[{SceneName}][v{GameVersion}]";
            return $"[{timeStr}][{levelStr}]{deviceInfo} {tagStr}{Message}\n{StackTrace}";
        }

        public string ToJson() => JsonUtility.ToJson(this);
    }

    /// <summary>
    /// 日志管理器
    /// 配置规则：启动时由 LogSettingsManager.Load() 从 ScriptableObject 注入；运行时 SetXxx 仅本次会话有效，不持久化。
    /// 想持久化请通过编辑器面板修改 LogSettingsAsset。
    /// </summary>
    public class LogManager : UnitySingleton<LogManager>, IModule
    {
        #region 配置（只读公开 + 受控写入）
        public static LogLevel MinLevel { get; private set; } = LogLevel.Debug;
        public static bool EnableConsole { get; private set; } = true;
        public static bool EnableFile { get; private set; } = true;
        public static bool EnableInEditor { get; private set; } = true;
        public static bool EnableSanitization { get; private set; } = true;
        public static bool EnableExceptionCatcher { get; private set; } = true;
        public static int SaveDays { get; private set; } = 7;
        public static bool RecordStackTrace { get; private set; } = true;
        public static LogFlushMode FlushMode { get; private set; } = LogFlushMode.DoubleBuffer;
        public static int BufferCountThreshold { get; private set; } = 10;
        public static float BufferTimeThreshold { get; private set; } = 3f;
        public static int MaxFileSizeBytes { get; private set; } = 10 * 1024 * 1024;
        #endregion

        #region 写入器
        private List<ILogWriter> _writers = new List<ILogWriter>();
        private FileLogWriter _fileWriter;
        #endregion

        #region 私有变量
        private string _logPath;
        private static readonly Dictionary<string, System.Text.RegularExpressions.Regex> _sanitizePatterns = new Dictionary<string, System.Text.RegularExpressions.Regex>();
        private static System.Text.RegularExpressions.Regex _sanitizeCombinedRegex;
        private static bool _isSanitizeInitialized = false;

        public static event Action<LogEntry> OnLogReceived;
        #endregion

        protected override void Awake()
        {
            base.Awake();
            if (_isDuplicate) return;
            LogSettingsManager.Load();
            InitSanitizePatterns();
            InitializeWriters();
            InitExceptionCatcher();

        }

        #region 配置入口
        /// <summary>
        /// 由 LogSettingsAsset 在启动或重载时调用，统一写入配置
        /// 业务代码请勿直接调用；运行时临时调整请用 SetMinLevel 等便利方法
        /// </summary>
        internal static void ApplySettings(LogSettingsAsset asset)
        {
            if (asset == null) return;
            MinLevel = asset.MinLevel;
            EnableConsole = asset.EnableConsole;
            EnableFile = asset.EnableFile;
            EnableInEditor = asset.EnableInEditor;
            FlushMode = asset.FlushMode;
            BufferCountThreshold = asset.BufferCountThreshold;
            BufferTimeThreshold = asset.BufferTimeThreshold;
            SaveDays = asset.SaveDays;
            MaxFileSizeBytes = asset.MaxFileSizeMB * 1024 * 1024;
            EnableSanitization = asset.EnableSanitization;
            RecordStackTrace = asset.RecordStackTrace;
            EnableExceptionCatcher = asset.EnableExceptionCatcher;
        }
        #endregion

        #region 初始化
        private static void InitSanitizePatterns()
        {
            if (_isSanitizeInitialized) return;
            _isSanitizeInitialized = true;
            _sanitizePatterns[@"IP"] = new System.Text.RegularExpressions.Regex(@"\b(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            _sanitizePatterns[@"MAC"] = new System.Text.RegularExpressions.Regex(@"\b([0-9A-Fa-f]{2}[:-]){5}([0-9A-Fa-f]{2})\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            _sanitizePatterns[@"Phone"] = new System.Text.RegularExpressions.Regex(@"\b1[3-9]\d{9}\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            _sanitizePatterns[@"Email"] = new System.Text.RegularExpressions.Regex(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            _sanitizePatterns[@"IDCard"] = new System.Text.RegularExpressions.Regex(@"\b[1-9]\d{5}(?:19|20)\d{2}(?:0[1-9]|1[0-2])(?:0[1-9]|[12]\d|3[01])\d{3}[\dXx]\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            _sanitizePatterns[@"Token"] = new System.Text.RegularExpressions.Regex(@"(?:token|key|secret|access_token|auth_token|refresh_token)\s*[:=]\s*[""']?([A-Za-z0-9_\-\.]+)[""']?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            _sanitizePatterns[@"URLParam"] = new System.Text.RegularExpressions.Regex(@"[?&](?:password|pwd|token|key|auth|session)=[^&\s]*", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            _sanitizePatterns[@"BankCard"] = new System.Text.RegularExpressions.Regex(@"\b\d{16,19}\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            _sanitizePatterns[@"IPv6"] = new System.Text.RegularExpressions.Regex(@"\b(?:[0-9a-fA-F]{1,4}:){7}[0-9a-fA-F]{1,4}\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            // 组合正则：先快速判断是否需要逐条脱敏，避免每条日志跑 9 次正则
            string combined = string.Join("|", _sanitizePatterns.Values.Select(p => p.ToString()));
            _sanitizeCombinedRegex = new System.Text.RegularExpressions.Regex(combined, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        private void InitializeWriters()
        {
            if (EnableConsole)
                _writers.Add(new ConsoleLogWriter { MinLevel = MinLevel });

            if (EnableFile)
            {
                bool shouldInitFile = EnableFile;
#if UNITY_EDITOR
                shouldInitFile = shouldInitFile && EnableInEditor;
#endif
                if (shouldInitFile)
                {
                    _logPath = Path.Combine(Application.persistentDataPath, "Logs");
                    string fileName = $"log_{DateTime.Now:yyyyMMdd}";
                    _fileWriter = new FileLogWriter(_logPath, fileName)
                    {
                        MinLevel = MinLevel,
                        Buffered = FlushMode == LogFlushMode.DoubleBuffer,
                        BufferCountThreshold = BufferCountThreshold,
                        BufferTimeThreshold = BufferTimeThreshold
                    };
                    _writers.Add(_fileWriter);
                    CleanOldLogs(_logPath);
                    UnityEngine.Debug.Log($"[LogManager] 文件日志已初始化，路径: {_logPath}");
                }
            }
        }

        private void CleanOldLogs(string directory)
        {
            if (!Directory.Exists(directory)) return;
            try
            {
                var files = new DirectoryInfo(directory).GetFiles("*.txt").OrderByDescending(f => f.CreationTime).Skip(SaveDays).ToList();
                foreach (var file in files) file.Delete();
            }
            catch (Exception ex) { UnityEngine.Debug.LogError($"[LogManager] 清理旧日志失败: {ex.Message}"); }
        }

        private void InitExceptionCatcher()
        {
            if (!EnableExceptionCatcher) return;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            Application.logMessageReceivedThreaded += OnLogMessageReceived;
#if UNITY_2020_1_OR_NEWER
            Application.lowMemory += OnLowMemory;
#endif
        }

        private void Update()
        {
            // 双缓冲模式下由 Unity 生命周期驱动文件日志按时间阈值落盘
            if (_fileWriter != null && _fileWriter.NeedsFlush())
            {
                _fileWriter.Flush();
            }
        }
        #endregion

        #region 公共方法
        public static void Debug(string message, string tag = "") => Log(LogLevel.Debug, message, tag);
        public static void Info(string message, string tag = "") => Log(LogLevel.Info, message, tag);
        public static void Warning(string message, string tag = "") => Log(LogLevel.Warning, message, tag);
        public static void Error(string message, string tag = "", Exception ex = null) => Log(LogLevel.Error, message, tag, ex?.StackTrace ?? "");
        public static void Fatal(string message, string tag = "", Exception ex = null) => Log(LogLevel.Fatal, message, tag, ex?.StackTrace ?? "");

        public static void Log(LogLevel level, string message, string tag = "", string stackTrace = "")
        {
            if (level < MinLevel) return;
            if (EnableSanitization) { message = Sanitize(message); stackTrace = Sanitize(stackTrace); }
            var entry = new LogEntry(level, message, stackTrace, tag);
            var instance = Instance;
            if (instance == null) return; // 退出阶段单例已销毁，避免 NRE
            foreach (var writer in instance._writers) { try { writer.Write(entry); } catch { } }
            try { OnLogReceived?.Invoke(entry); } catch { }

            // 远程上传（唯一上传通道；UploadLog 内部按 EnableUpload / UploadLevel 过滤）
            LogUploader.UploadLog(message, stackTrace, level);
        }

        public static string Sanitize(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            if (!_isSanitizeInitialized) InitSanitizePatterns();
            // 快速路径：无敏感信息命中时直接返回，避免每条日志 9 次正则
            if (_sanitizeCombinedRegex != null && !_sanitizeCombinedRegex.IsMatch(input)) return input;
            string result = input;
            foreach (var pattern in _sanitizePatterns)
            {
                result = pattern.Value.Replace(result, match =>
                {
                    if (pattern.Key == @"Token")
                    {
                        if (match.Groups.Count > 1) { string token = match.Groups[1].Value; if (token.Length > 6) return match.Value.Replace(token, token.Substring(0, 3) + "***" + token.Substring(token.Length - 3)); }
                        return match.Value.Substring(0, match.Value.Length / 2) + "***";
                    }
                    else if (pattern.Key == @"URLParam") return match.Value.Substring(0, match.Value.IndexOf('=') + 1) + "***";
                    else if (pattern.Key == @"Phone" || pattern.Key == @"IDCard" || pattern.Key == @"BankCard") { string original = match.Value; int len = original.Length; return original.Substring(0, 3) + new string('*', len - 6) + original.Substring(len - 3); }
                    else return match.Value.Substring(0, 2) + "***";
                });
            }
            return result;
        }

        /// <summary>
        /// 运行时调整最小日志级别（本次会话有效，不持久化）
        /// </summary>
        public static void SetMinLevel(LogLevel level)
        {
            MinLevel = level;
            var instance = Instance;
            if (instance == null) return;
            foreach (var writer in instance._writers) writer.MinLevel = level;
        }

        /// <summary>
        /// 运行时启停文件日志（本次会话有效，不持久化）
        /// </summary>
        public static void SetFileLogging(bool enable) => EnableFile = enable;

        /// <summary>
        /// 运行时启停 Console 输出（本次会话有效，不持久化）
        /// </summary>
        public static void SetConsoleLogging(bool enable) => EnableConsole = enable;

        public string GetCurrentLogPath() => _fileWriter?.GetFilePath() ?? string.Empty;
        public string[] GetAllLogFiles() { string logDir = Path.Combine(Application.persistentDataPath, "Logs"); return Directory.Exists(logDir) ? Directory.GetFiles(logDir, "*.txt") : Array.Empty<string>(); }
        public void Flush() { foreach (var writer in _writers) { try { writer.Flush(); } catch { } } }
        public void Close() { foreach (var writer in _writers) { try { writer.Close(); } catch { } } _writers.Clear(); }
        #endregion

        #region 异常处理
        private void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        { if (type == LogType.Exception || type == LogType.Assert) { LogLevel level = type == LogType.Assert ? LogLevel.Warning : LogLevel.Error; Log(level, Sanitize(condition), "Unity", RecordStackTrace ? Sanitize(stackTrace) : ""); } }
        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e) { Exception ex = e.ExceptionObject as Exception; Log(LogLevel.Fatal, $"Unhandled Exception: {ex?.Message ?? "Unknown"}", "Crash", RecordStackTrace ? (ex?.StackTrace ?? "") : ""); Flush(); }
#if UNITY_2020_1_OR_NEWER
        private void OnLowMemory() { Log(LogLevel.Warning, "Low memory detected!", "Memory"); ResourcesManager.Instance?.ReleaseUnused(); }
#endif
        #endregion

        #region 生命周期
        private void OnApplicationPause(bool pauseStatus) { if (pauseStatus) { Info("应用进入后台", "Lifecycle"); Flush(); } else Info("应用回到前台", "Lifecycle"); }
        protected override void OnApplicationQuit() { Info("应用退出", "Lifecycle"); Flush(); Close(); }
        protected override void OnDestroy() { Flush(); Close(); Application.logMessageReceivedThreaded -= OnLogMessageReceived; AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException; base.OnDestroy(); }
        #endregion

        #region IModule 实现

        public string DisplayName => GetType().Name;
        public UniTask InitializeAsync() => UniTask.CompletedTask;
        public void Tick(float deltaTime)
        {
            if (_fileWriter != null && _fileWriter.NeedsFlush())
                _fileWriter.Flush();
        }
        public void Dispose() { Flush(); Close(); }

        #endregion
    }

    /// <summary>
    /// 远程日志上传器
    /// </summary>
    public static class LogUploader
    {
        public enum ServerType { HTTP = 0, WebDAV = 1 }
        public static bool EnableUpload { get; internal set; } = false;
        public static ServerType Type { get; internal set; } = ServerType.HTTP;
        public static string UploadUrl { get; internal set; } = "";
        public static string Username { get; internal set; } = "";
        public static string Password { get; internal set; } = "";
        public static LogLevel UploadLevel { get; internal set; } = LogLevel.Fatal;
        public static int ConnectionTimeout { get; internal set; } = 5;

        internal static void ApplySettings(LogSettingsAsset asset)
        {
            if (asset == null) return;
            EnableUpload = asset.EnableUpload;
            Type = asset.ServerType;
            UploadUrl = asset.UploadUrl;
            Username = asset.Username;
            Password = asset.Password;
            UploadLevel = asset.UploadLevel;
            ConnectionTimeout = asset.ConnectionTimeout;
        }

        // 指数退避参数
        private const int MAX_RETRIES = 5;
        private const float BASE_RETRY_DELAY = 2f;
        private const float MAX_RETRY_DELAY = 60f;

        private static readonly Queue<string> _pendingLogs = new Queue<string>();
        private static readonly object _lockObj = new object();
        private static bool _isUploading = false;
        private static float _retryDelay = BASE_RETRY_DELAY;
        private static int _consecutiveFailures = 0;

        public static void UploadLog(string message, string stackTrace, LogLevel level)
        {
            if (!EnableUpload || string.IsNullOrEmpty(UploadUrl) || level < UploadLevel) return;
            string content = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}][{level}] {message}\n{stackTrace}\n";
            lock (_lockObj) { _pendingLogs.Enqueue(content); }
            StartUpload();
        }

        private static void StartUpload()
        {
            if (_isUploading) return;
            var mono = MonoManager.Instance;
            if (mono != null) mono.StartCoroutine(UploadCoroutine());
        }

        private static IEnumerator UploadCoroutine()
        {
            if (_isUploading) yield break;
            _isUploading = true;

            List<string> batch = new List<string>();
            lock (_lockObj) { while (_pendingLogs.Count > 0 && batch.Count < 10) batch.Add(_pendingLogs.Dequeue()); }
            if (batch.Count == 0) { _isUploading = false; yield break; }

            string content = string.Join("\n", batch);
            bool success = false;

            if (Type == ServerType.HTTP) yield return UploadHttp(content, r => success = r);
            else yield return UploadWebDAV(content, r => success = r);

            if (!success)
            {
                _consecutiveFailures++;
                if (_consecutiveFailures > MAX_RETRIES)
                {
                    // 连续失败超过上限：丢弃该批日志并重置，避免队列无限膨胀
                    Debug.LogError($"[LogUploader] 连续上传失败 {MAX_RETRIES} 次，丢弃 {batch.Count} 条日志");
                    _consecutiveFailures = 0;
                    _retryDelay = BASE_RETRY_DELAY;
                    _isUploading = false;
                    yield break;
                }

                // 失败重入队 + 指数退避后主动重试
                lock (_lockObj) { foreach (var log in batch) _pendingLogs.Enqueue(log); }
                Debug.LogWarning($"[LogUploader] 上传失败，{_retryDelay:0.#}s 后重试");
                yield return new WaitForSeconds(_retryDelay);
                _retryDelay = Mathf.Min(_retryDelay * 2, MAX_RETRY_DELAY);
                _isUploading = false;
                StartUpload();
                yield break;
            }

            // 上传成功：重置退避状态
            _consecutiveFailures = 0;
            _retryDelay = BASE_RETRY_DELAY;
            Debug.Log($"[LogUploader] 上传成功: {batch.Count} 条");
            _isUploading = false;
        }

        private static IEnumerator UploadHttp(string content, Action<bool> callback)
        {
            if (string.IsNullOrEmpty(UploadUrl)) { callback?.Invoke(false); yield break; }
            byte[] bodyData = Encoding.UTF8.GetBytes(content);
            var request = new UnityEngine.Networking.UnityWebRequest(UploadUrl, "POST") { timeout = ConnectionTimeout * 3 };
            request.uploadHandler = new UnityEngine.Networking.UploadHandlerRaw(bodyData);
            request.SetRequestHeader("Content-Type", "text/plain; charset=utf-8");
            yield return request.SendWebRequest();
            callback?.Invoke(request.result == UnityEngine.Networking.UnityWebRequest.Result.Success);
        }

        private static IEnumerator UploadWebDAV(string content, Action<bool> callback)
        {
            if (string.IsNullOrEmpty(UploadUrl)) { callback?.Invoke(false); yield break; }
            string fileName = $"log_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            string url = $"{UploadUrl.TrimEnd('/')}/GameLogs/{fileName}";
            byte[] bodyData = Encoding.UTF8.GetBytes(content);
            var request = new UnityEngine.Networking.UnityWebRequest(url, "PUT") { timeout = ConnectionTimeout * 3 };
            request.uploadHandler = new UnityEngine.Networking.UploadHandlerRaw(bodyData);
            if (!string.IsNullOrEmpty(Username)) { string auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Username}:{Password}")); request.SetRequestHeader("Authorization", $"Basic {auth}"); }
            request.SetRequestHeader("Content-Type", "application/octet-stream");
            yield return request.SendWebRequest();
            callback?.Invoke(request.result == UnityEngine.Networking.UnityWebRequest.Result.Success);
        }

        public static int GetPendingCount() { lock (_lockObj) return _pendingLogs.Count; }
        public static bool CheckConnection() => !string.IsNullOrEmpty(UploadUrl);
        public static bool TestConnectionSync() => CheckConnection();
        public static void ForceRetry() { if (EnableUpload && !string.IsNullOrEmpty(UploadUrl)) { var mono = MonoManager.Instance; if (mono != null) mono.StartCoroutine(UploadLocalLogs()); } }

        private static IEnumerator UploadLocalLogs()
        {
            string logDir = Path.Combine(Application.persistentDataPath, "Logs");
            if (!Directory.Exists(logDir)) yield break;
            string[] files = Directory.GetFiles(logDir, "*.txt");
            foreach (string file in files)
            {
                string content = "";
                try { content = File.ReadAllText(file); } catch { continue; }
                bool success = false;
                if (Type == ServerType.HTTP) yield return UploadHttp(content, r => success = r);
                else yield return UploadWebDAV(content, r => success = r);
                if (success) Debug.Log($"[LogUploader] 上传成功: {Path.GetFileName(file)}");
                yield return new WaitForSeconds(0.5f);
            }
        }
    }

    public static class LogExtensions
    {
        public static void D(this object obj, string message) => LogManager.Debug(message, obj?.GetType().Name ?? "");
        public static void I(this object obj, string message) => LogManager.Info(message, obj?.GetType().Name ?? "");
        public static void W(this object obj, string message) => LogManager.Warning(message, obj?.GetType().Name ?? "");
        public static void E(this object obj, string message, Exception ex = null) => LogManager.Error(message, obj?.GetType().Name ?? "", ex);
        public static void F(this object obj, string message, Exception ex = null) => LogManager.Fatal(message, obj?.GetType().Name ?? "", ex);
    }
}
