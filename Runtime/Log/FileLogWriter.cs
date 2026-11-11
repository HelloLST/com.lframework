// FileLogWriter.cs
// 文件日志写入器（支持双缓冲，配置阈值由 LogManager 驱动落盘）

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace LFramework
{
    /// <summary>
    /// 文件日志写入器。
    /// Buffered=true 时先写入内存缓冲，达到条数/时间阈值再落盘，避免每条日志一次磁盘 IO。
    ///
    /// 打磨台账 D4：<c>LogManager.MaxFileSizeBytes</c> 此前**只被赋值与回读，从未被本类使用** ——
    /// 一个暴露给策划配置、在设置面板里可改、却完全不生效的开关。日志文件会无限增长，
    /// 直到把设备存储写满（移动端尤其致命）。现在按大小轮转：
    /// 超过上限就把当前文件重命名为 <c>xxx.1.txt</c>，开新文件续写。
    /// </summary>
    public class FileLogWriter : ILogWriter
    {
        private readonly string _directory;
        private readonly string _filePrefix;
        private string _filePath;
        private StreamWriter _writer;
        private readonly object _lock = new object();
        private readonly List<LogEntry> _buffer = new List<LogEntry>();
        private float _lastWriteTime;

        /// <summary>是否启用双缓冲（由 LogManager.FlushMode 配置）</summary>
        public bool Buffered { get; set; } = true;

        /// <summary>条数阈值：缓冲达到该数量立即落盘</summary>
        public int BufferCountThreshold { get; set; } = 10;

        /// <summary>时间阈值（秒）：缓冲非空且超过该时长由 LogManager.Tick 落盘</summary>
        public float BufferTimeThreshold { get; set; } = 3f;

        public LogLevel MinLevel { get; set; } = LogLevel.Debug;

        /// <summary>单文件大小上限（字节）。&lt;= 0 表示不限制（打磨台账 D4）。</summary>
        public int MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;

        /// <summary>轮转后保留的备份份数（.1 / .2 ...），超过则删最旧。</summary>
        public int MaxRollingFiles { get; set; } = 5;

        public FileLogWriter(string directory, string fileName = null)
        {
            _directory = directory;
            _filePrefix = fileName ?? $"log_{DateTime.Now:yyyyMMdd}";

            if (!Directory.Exists(_directory))
            {
                Directory.CreateDirectory(_directory);
            }

            _filePath = Path.Combine(_directory, $"{_filePrefix}.txt");
            OpenWriter(append: true);

            // 注意：不能在这里调 Write(...)，那会重新进 _lock（lock 虽可重入，
            // 但会让"启动"这条日志走一遍缓冲逻辑，语义含糊）。直接写一行。
            WriteLineDirect(FormatStartupBanner());
        }

        private static LogEntry FormatStartupBanner()
            => new LogEntry(LogLevel.Info, "System", "========== 日志系统启动 ==========");

        private void OpenWriter(bool append)
        {
            _writer = new StreamWriter(_filePath, append, Encoding.UTF8)
            {
                AutoFlush = false  // 使用缓冲模式，提高性能
            };
        }

        /// <summary>直接写一行并刷新（绕过缓冲，用于生命周期标记与轮转前的收尾）。</summary>
        private void WriteLineDirect(LogEntry entry)
        {
            try
            {
                _writer.WriteLine(entry.FormatWithDevice());
                _writer.Flush();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FileLogWriter] 直接写入失败: {ex.Message}");
            }
        }

        public void Write(LogEntry entry)
        {
            if (entry.Level < MinLevel) return;

            lock (_lock)
            {
                try
                {
                    if (!Buffered)
                    {
                        // 实时模式：立即写盘
                        _writer.WriteLine(entry.FormatWithDevice());
                        _writer.Flush();
                        RotateIfNeeded();
                        return;
                    }

                    _buffer.Add(entry);
                    _lastWriteTime = Time.realtimeSinceStartup;
                    if (_buffer.Count >= BufferCountThreshold)
                    {
                        FlushLocked();
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[FileLogWriter] 写入失败: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 是否需要按时间阈值落盘（由 LogManager.Tick 每帧驱动，双缓冲模式有效）
        /// </summary>
        public bool NeedsFlush()
        {
            lock (_lock)
            {
                return Buffered && _buffer.Count > 0
                    && (Time.realtimeSinceStartup - _lastWriteTime) >= BufferTimeThreshold;
            }
        }

        public void Flush()
        {
            lock (_lock)
            {
                try
                {
                    FlushLocked();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[FileLogWriter] 刷新失败: {ex.Message}");
                }
            }
        }

        public void Close()
        {
            lock (_lock)
            {
                try
                {
                    // 收尾标记直接写，不走缓冲 —— 防止"关闭"日志被 add 进 buffer 后
                    // 紧接的 FlushLocked 顺序变得难以推理。
                    WriteLineDirect(new LogEntry(LogLevel.Info, "System", "========== 日志系统关闭 =========="));
                    FlushLocked();
                    _writer.Close();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[FileLogWriter] 关闭失败: {ex.Message}");
                }
            }
        }

        public string GetFilePath() => _filePath;

        private void FlushLocked()
        {
            if (_buffer.Count > 0)
            {
                foreach (var entry in _buffer)
                {
                    _writer.WriteLine(entry.FormatWithDevice());
                }
                _buffer.Clear();
            }
            _writer.Flush();
            RotateIfNeeded();
        }

        /// <summary>
        /// 按大小轮转（打磨台账 D4）。超过 <see cref="MaxFileSizeBytes"/> 时：
        /// 关当前文件 → 依次后移旧备份（.4→.5, .3→.4 …）→ 当前文件改名为 .1 → 重开空文件续写。
        ///
        /// 为什么要在 Flush 之后判断：只有落盘后文件长度才是真实的，
        /// 否则会拿"缓冲未写"的长度判断，导致轮转时机偏晚。
        /// </summary>
        private void RotateIfNeeded()
        {
            if (MaxFileSizeBytes <= 0) return;

            long length;
            try
            {
                // 必须真实取磁盘长度：_writer 的 BaseStream.Length 在缓冲下不可靠
                length = new FileInfo(_filePath).Length;
            }
            catch
            {
                return;   // 取不到就跳过本轮，不影响日志功能
            }

            if (length < MaxFileSizeBytes) return;

            try
            {
                _writer.Close();

                // 后移旧备份：从最旧的一份开始删，避免越界
                int keep = Mathf.Max(1, MaxRollingFiles);
                string oldest = $"{_filePath}.{keep}";
                if (File.Exists(oldest)) File.Delete(oldest);

                for (int i = keep - 1; i >= 1; i--)
                {
                    string from = $"{_filePath}.{i}";
                    string to = $"{_filePath}.{i + 1}";
                    if (File.Exists(from))
                    {
                        if (File.Exists(to)) File.Delete(to);
                        File.Move(from, to);
                    }
                }

                string first = $"{_filePath}.1";
                if (File.Exists(first)) File.Delete(first);
                File.Move(_filePath, first);

                OpenWriter(append: false);
                WriteLineDirect(new LogEntry(
                    LogLevel.Info, "System",
                    $"========== 日志已轮转（上一份 {length / 1024} KB）=========="));
            }
            catch (Exception ex)
            {
                // 轮转失败绝不能让日志功能挂掉：尽力重开原路径续写
                Debug.LogError($"[FileLogWriter] 日志轮转失败: {ex.Message}");
                try
                {
                    if (_writer.BaseStream == null || !_writer.BaseStream.CanWrite)
                    {
                        OpenWriter(append: true);
                    }
                }
                catch { /* 已经无能为力，放弃本轮 */ }
            }
        }
    }
}
