// FileLogWriter.cs
// 文件日志写入器（支持双缓冲，配置阈值由 LogManager 驱动落盘）

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace LFramework
{
    /// <summary>
    /// 文件日志写入器
    /// Buffered=true 时先写入内存缓冲，达到条数/时间阈值再落盘，避免每条日志一次磁盘 IO。
    /// </summary>
    public class FileLogWriter : ILogWriter
    {
        private readonly string _filePath;
        private readonly StreamWriter _writer;
        private readonly object _lock = new object();
        private readonly List<LogEntry> _buffer = new List<LogEntry>();
        private float _lastWriteTime;

        /// <summary>是否启用双缓冲（由 LogManager.FlushMode 配置）</summary>
        public bool Buffered { get; set; } = true;

        /// <summary>条数阈值：缓冲达到该数量立即落盘</summary>
        public int BufferCountThreshold { get; set; } = 10;

        /// <summary>时间阈值（秒）：缓冲非空且超过该时长由 LogManager.Update 落盘</summary>
        public float BufferTimeThreshold { get; set; } = 3f;

        public LogLevel MinLevel { get; set; } = LogLevel.Debug;

        public FileLogWriter(string directory, string fileName = null)
        {
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string prefix = fileName ?? $"log_{DateTime.Now:yyyyMMdd}";
            _filePath = Path.Combine(directory, $"{prefix}.txt");

            _writer = new StreamWriter(_filePath, true, Encoding.UTF8)
            {
                AutoFlush = false  // 使用缓冲模式，提高性能
            };

            Write(new LogEntry(LogLevel.Info, "System", "========== 日志系统启动 =========="));
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
        /// 是否需要按时间阈值落盘（由 LogManager.Update 每帧驱动，双缓冲模式有效）
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
                    Write(new LogEntry(LogLevel.Info, "System", "========== 日志系统关闭 =========="));
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
            foreach (var entry in _buffer)
            {
                _writer.WriteLine(entry.FormatWithDevice());
            }
            _buffer.Clear();
            _writer.Flush();
        }
    }
}
