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
    public class LogManager : UnitySingleton<LogManager>, IModule, ITickable
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

        /// <summary>
        /// 是否已尝试过"按需建立 writer"（打磨台账 D1）。
        /// 只尝试一次：真正的失败（如目录不可写）不该每写一条日志就重试一遍。
        /// </summary>
        private bool _writersEnsured;
        #endregion

        #region 私有变量
        private string _logPath;
        private static readonly Dictionary<string, System.Text.RegularExpressions.Regex> _sanitizePatterns = new Dictionary<string, System.Text.RegularExpressions.Regex>();
        private static System.Text.RegularExpressions.Regex _sanitizeCombinedRegex;
        private static bool _isSanitizeInitialized = false;

        // 启动缓冲：LogManager 模块初始化前的日志先进缓冲、初始化时统一回放。
        // 写日志不再通过 Instance 懒创建管理器（避免在模块清单外催生实例 + 退出阶段 NRE）—— Phase 5 / 5.4
        private static readonly Queue<LogEntry> _startupBuffer = new Queue<LogEntry>();
        private const int StartupBufferCapacity = 256;

        public static event Action<LogEntry> OnLogReceived;

        /// <summary>
        /// 进入 Play 时清理跨会话静态状态（Phase 9）。
        ///
        /// 关闭 Domain Reload 时以下静态字段会从上一会话残留，导致新会话的错误现象：
        /// - <c>_startupBuffer</c>：上一会话没来得及回放的日志会混进新会话首屏，
        ///   且占着容量把真正属于本会话的启动日志挤掉；
        /// - <c>OnLogReceived</c>：上一会话被销毁对象的订阅仍挂在委托上，
        ///   本会话首次派发就会撞上已销毁的 MonoBehaviour（Unity 假 null）；
        /// - <c>LogUploader</c> 的待发队列 / 重试状态：会把上一会话的日志再传一遍，
        ///   且 <c>_isUploading</c> 若停在 true，本会话的上传永远起不来。
        ///
        /// <c>_sanitizePatterns</c> / <c>_sanitizeCombinedRegex</c> 不清理 ——
        /// 它们是纯函数性缓存（正则对象，不持有 Unity 对象），重建反而浪费。
        /// <see cref="LogManager"/> 继承 MonoBehaviour（UnitySingleton），不是开放泛型类型，
        /// 所以这里的引擎回调会正常触发，无需走 RuntimeSessionReset。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticStateForNewSession()
        {
            _startupBuffer.Clear();
            OnLogReceived = null;

            // 上传通道：停在上一个会话的队列与重试状态都要复位
            LogUploader.ResetForNewSession();
        }
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
            // 可重入（打磨台账 D4 连带）：此前重复调用会不断往 _writers 里追加，
            // 导致同一条日志被写多遍、旧文件句柄泄漏。运行时改配置后重建 writer
            // 是合理需求，这里先释放旧 writer 再建。
            if (_writers.Count > 0 || _fileWriter != null)
            {
                foreach (var w in _writers)
                {
                    try { w.Flush(); w.Close(); } catch { /* 释放失败不阻断重建 */ }
                }
                _writers.Clear();
                _fileWriter = null;
            }

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
                        BufferTimeThreshold = BufferTimeThreshold,
                        // 打磨台账 D4：此前 MaxFileSizeBytes 只被赋值与回读，
                        // 从未传给 writer，配置了却不生效。这里补上。
                        MaxFileSizeBytes = MaxFileSizeBytes,
                        MaxRollingFiles = Mathf.Max(1, SaveDays)
                    };
                    _writers.Add(_fileWriter);
                    CleanOldLogs(_logPath);
                    UnityEngine.Debug.Log($"[LogManager] 文件日志已初始化，路径: {_logPath}");
                }
            }

            // 建立完成即视为"已尝试过"（无论成功与否），
            // 避免 D1 的按需路径对同一个失败的配置反复重试
            _writersEnsured = true;
        }

        /// <summary>
        /// 按当前配置重建所有写入器（打磨台账 D4 连带）。
        /// 运行时改了 <see cref="MaxFileSizeBytes"/> / 刷盘阈值 / 级别后调用即可生效，
        /// 不必重启。此前配置只在 Awake 生效一次，改完必须重启才看到效果。
        /// </summary>
        public void ReinitializeWriters()
        {
            // 重置"按需建立"标记：下次写入时若发现 writer 仍为空，允许再尝试一次（D1）
            _writersEnsured = false;
            InitializeWriters();
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

            // 不经 Instance 懒创建管理器（避免在模块清单外催生实例 + 退出阶段 NRE）：
            // 未就绪时进入启动缓冲，InitializeAsync 时统一回放 —— Phase 5 / 5.4
            if (!UnitySingleton<LogManager>.TryGetExisting(out var instance))
            {
                // 缓冲满时溢出到 Unity 控制台，而不是只丢最旧一条了事：
                // 若本会话里 LogManager 永远不会被创建（典型：EditMode 测试中 AddComponent 不触发
                // Awake，或模块清单里漏配 LogManager），缓冲永远不会回放，日志会静默消失，
                // 排查时看起来"什么都没发生"。宁可吵一点，也不能丢。
                if (_startupBuffer.Count >= StartupBufferCapacity)
                {
                    WriteFallbackToUnityConsole(entry);
                    return;
                }
                _startupBuffer.Enqueue(entry);
                return;
            }

            WriteToWriters(instance, entry);
            try { OnLogReceived?.Invoke(entry); } catch { }

            // 远程上传（唯一上传通道；UploadLog 内部按 EnableUpload / UploadLevel 过滤）
            LogUploader.UploadLog(message, stackTrace, level);
        }

        private static void WriteToWriters(LogManager instance, LogEntry entry)
        {
            // 打磨台账 D1：writer 只在 Awake 里建立。若 LogManager 被走到了**但 Awake 没执行**
            // （典型：EditMode 里 AddComponent 不触发 Awake；或模块清单漏配后有人手工 new），
            // _writers 就是空的 —— 所有日志静默消失，控制台一片安静，排查时完全看不出问题。
            //
            // 这里补一条**按需建立**路径：发现没有 writer 就建一次。
            // 注意不能每次写日志都调（那会在同一帧反复重建文件句柄），
            // 用 _writersEnsured 标记只尝试一次；真的建不起来也不反复尝试。
            if (instance._writers.Count == 0 && !instance._writersEnsured)
            {
                instance._writersEnsured = true;   // 先置位：InitializeWriters 内部会打 Debug.Log，防递归
                try
                {
                    instance.InitializeWriters();
                    if (instance._writers.Count == 0)
                    {
                        // 配置里两个输出都关了：这本身是合法配置，不该告警
                        UnityEngine.Debug.Log(
                            "[LogManager] 控制台与文件输出均未启用，日志只会进 OnLogReceived / 远程上传。");
                    }
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogError($"[LogManager] 按需建立 writer 失败，后续日志将无处可写：{e}");
                }
            }

            foreach (var writer in instance._writers) { try { writer.Write(entry); } catch { } }
        }

        /// <summary>
        /// 启动缓冲溢出时的兜底输出：直接用 Unity 控制台写出，不走 writer。
        /// 只在"缓冲已满且 LogManager 仍未就绪"时触发，用于避免日志在无管理器的会话里静默消失。
        /// </summary>
        private static void WriteFallbackToUnityConsole(LogEntry entry)
        {
            string message = $"[{entry.Time:HH:mm:ss.fff}][{entry.Level}]{entry.Message}";
            switch (entry.Level)
            {
                case LogLevel.Warning:
                    // 必须全限定：本类内的 Debug(string,string) 方法会遮蔽 using 别名 Debug = UnityEngine.Debug
                    UnityEngine.Debug.LogWarning(message);
                    break;
                case LogLevel.Error:
                case LogLevel.Fatal:
                    UnityEngine.Debug.LogError(message);
                    break;
                default:
                    UnityEngine.Debug.Log(message);
                    break;
            }
        }

        /// <summary>回放启动缓冲（LogManager 模块初始化时调用一次）。</summary>
        private void FlushStartupBuffer()
        {
            while (_startupBuffer.Count > 0)
            {
                WriteToWriters(this, _startupBuffer.Dequeue());
            }
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
        public async UniTask InitializeAsync()
        {
            // 模块初始化即就绪：回放启动期缓冲的日志（Phase 5 / 5.4）
            FlushStartupBuffer();
            await UniTask.CompletedTask;
        }
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

        /// <summary>
        /// 待发队列的默认上限（D3 修复）。
        ///
        /// 原实现是无限 Queue：服务器不可达时，每条超阈值日志都往队列里塞，
        /// 而消费端因为退避重试一直卡在第一批上原地打转 —— 生产端快、消费端停，
        /// 队列就是纯粹的内存泄漏。灾难场景（服务器挂了 + 疯狂报错）下，
        /// 日志系统自己会成为压死进程的最后一根稻草。
        ///
        /// 上限触发后丢弃**最旧**的日志（保留最近的现场更有排查价值），并记一次计数，
        /// 后续可通过 <see cref="GetDroppedCount"/> 观测到底丢了多少。
        /// </summary>
        public const int DefaultMaxPendingLogs = 500;

        private static readonly LogUploadQueue _pendingLogs = new LogUploadQueue(DefaultMaxPendingLogs);
        private static bool _isUploading = false;
        private static float _retryDelay = BASE_RETRY_DELAY;
        private static int _consecutiveFailures = 0;
        private static bool _dropWarned;

        /// <summary>待发队列上限。&lt;= 0 表示不限制（恢复旧行为，不建议）。</summary>
        public static int MaxPendingLogs
        {
            get { return _pendingLogs.Capacity; }
            set { _pendingLogs.Capacity = value; }
        }

        /// <summary>因队列超限被丢弃的日志条数（会话内累计）。</summary>
        public static int GetDroppedCount() { return _pendingLogs.DroppedCount; }

        /// <summary>
        /// 进入 Play 时复位上传通道（Phase 9）。由 <see cref="LogManager.ResetStaticStateForNewSession"/> 调用。
        ///
        /// 关闭 Domain Reload 时这些静态字段会跨会话残留：
        /// - <c>_pendingLogs</c>：上一会话未发出的日志会在本会话再传一遍（重复 + 误导排查）；
        /// - <c>_isUploading</c>：上一会话若停在 true（例如 upload loop 被域重载打断），
        ///   本会话的 <c>StartUpload</c> 会直接 return，**上传功能永久失效且无任何提示**；
        /// - <c>_retryDelay</c> / <c>_consecutiveFailures</c>：退避状态带进新会话。
        /// </summary>
        internal static void ResetForNewSession()
        {
            _pendingLogs.Clear();
            // _isUploading 停在 true 说明上一会话的 UploadLoop 已被域重载杀死，
            // 本会话必须允许重新启动；真正的并发启动由 _isUploading 自身守卫。
            _isUploading = false;
            _retryDelay = BASE_RETRY_DELAY;
            _consecutiveFailures = 0;
            _dropWarned = false;
        }

        public static void UploadLog(string message, string stackTrace, LogLevel level)
        {
            if (!EnableUpload || string.IsNullOrEmpty(UploadUrl) || level < UploadLevel) return;
            string content = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}][{level}] {message}\n{stackTrace}\n";

            int dropped = _pendingLogs.Enqueue(content);

            // 只在本会话第一次丢弃时提示一次：否则日志系统会变成自己的日志洪水来源
            if (dropped > 0 && !_dropWarned)
            {
                _dropWarned = true;
                LogManager.Warning(
                    $"[LogUploader] 待发队列超过上限 {MaxPendingLogs}，已丢弃最旧的日志（后续丢弃只计数，见 GetDroppedCount）");
            }

            StartUpload();
        }

        private static void StartUpload()
        {
            if (_isUploading) return;
            UploadLoop().Forget();
        }

        /// <summary>上传主循环（UniTask 驱动；失败原地指数退避重试，Phase 5 / 5.3 去协程）。</summary>
        private static async UniTaskVoid UploadLoop()
        {
            if (_isUploading) return;
            _isUploading = true;
            try
            {
                while (true)
                {
                    List<string> batch = _pendingLogs.DequeueBatch(10);
                    if (batch.Count == 0) return;

                    string content = string.Join("\n", batch);
                    bool success = Type == ServerType.HTTP
                        ? await UploadHttpAsync(content)
                        : await UploadWebDAVAsync(content);

                    if (!success)
                    {
                        _consecutiveFailures++;
                        if (_consecutiveFailures > MAX_RETRIES)
                        {
                            // 连续失败超过上限：丢弃该批日志并重置，避免队列无限膨胀
                            Debug.LogError($"[LogUploader] 连续上传失败 {MAX_RETRIES} 次，丢弃 {batch.Count} 条日志 LOGSCAN_EXPECTED");
                            _consecutiveFailures = 0;
                            _retryDelay = BASE_RETRY_DELAY;
                            return;
                        }

                        // 失败重入队 + 指数退避后原地重试。
                        // D3：必须回到**队首** —— 它们是当前最旧的现场，放队尾会被新日志挤掉。
                        _pendingLogs.RequeueAtHead(batch);
                        Debug.LogWarning($"[LogUploader] 上传失败，{_retryDelay:0.#}s 后重试");
                        await UniTask.Delay(TimeSpan.FromSeconds(_retryDelay));
                        _retryDelay = Mathf.Min(_retryDelay * 2, MAX_RETRY_DELAY);
                        continue;
                    }

                    // 上传成功：重置退避状态
                    _consecutiveFailures = 0;
                    _retryDelay = BASE_RETRY_DELAY;
                    Debug.Log($"[LogUploader] 上传成功: {batch.Count} 条");
                }
            }
            finally { _isUploading = false; }
        }

        private static async UniTask<bool> UploadHttpAsync(string content)
        {
            if (string.IsNullOrEmpty(UploadUrl)) return false;
            byte[] bodyData = Encoding.UTF8.GetBytes(content);
            var request = new UnityEngine.Networking.UnityWebRequest(UploadUrl, "POST") { timeout = ConnectionTimeout * 3 };
            request.uploadHandler = new UnityEngine.Networking.UploadHandlerRaw(bodyData);
            request.SetRequestHeader("Content-Type", "text/plain; charset=utf-8");
            await request.SendWebRequest();
            return request.result == UnityEngine.Networking.UnityWebRequest.Result.Success;
        }

        private static async UniTask<bool> UploadWebDAVAsync(string content)
        {
            if (string.IsNullOrEmpty(UploadUrl)) return false;
            string fileName = $"log_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            string url = $"{UploadUrl.TrimEnd('/')}/GameLogs/{fileName}";
            byte[] bodyData = Encoding.UTF8.GetBytes(content);
            var request = new UnityEngine.Networking.UnityWebRequest(url, "PUT") { timeout = ConnectionTimeout * 3 };
            request.uploadHandler = new UnityEngine.Networking.UploadHandlerRaw(bodyData);
            if (!string.IsNullOrEmpty(Username)) { string auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Username}:{Password}")); request.SetRequestHeader("Authorization", $"Basic {auth}"); }
            request.SetRequestHeader("Content-Type", "application/octet-stream");
            await request.SendWebRequest();
            return request.result == UnityEngine.Networking.UnityWebRequest.Result.Success;
        }

        public static int GetPendingCount() { return _pendingLogs.Count; }
        public static bool CheckConnection() => !string.IsNullOrEmpty(UploadUrl);
        public static bool TestConnectionSync() => CheckConnection();
        public static void ForceRetry() { if (EnableUpload && !string.IsNullOrEmpty(UploadUrl)) UploadLocalLogsAsync().Forget(); }

        private static async UniTaskVoid UploadLocalLogsAsync()
        {
            string logDir = Path.Combine(Application.persistentDataPath, "Logs");
            if (!Directory.Exists(logDir)) return;
            string[] files = Directory.GetFiles(logDir, "*.txt");
            foreach (string file in files)
            {
                string content = "";
                try { content = File.ReadAllText(file); } catch { continue; }
                bool success = Type == ServerType.HTTP
                    ? await UploadHttpAsync(content)
                    : await UploadWebDAVAsync(content);
                if (success) Debug.Log($"[LogUploader] 上传成功: {Path.GetFileName(file)}");
                await UniTask.Delay(TimeSpan.FromSeconds(0.5f));
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
