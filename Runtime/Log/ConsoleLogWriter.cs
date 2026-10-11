// ConsoleLogWriter.cs
// 控制台日志写入器

using UnityEngine;

namespace LFramework
{
    /// <summary>
    /// 控制台日志写入器
    /// </summary>
    public class ConsoleLogWriter : ILogWriter
    {
        public LogLevel MinLevel { get; set; } = LogLevel.Debug;

        public void Write(LogEntry entry)
        {
            if (entry.Level < MinLevel) return;

            string message = $"[{entry.Time:HH:mm:ss.fff}][{entry.Level}] {entry.Message}";

            switch (entry.Level)
            {
                case LogLevel.Debug:
                case LogLevel.Info:
                    Debug.Log(message);
                    break;
                case LogLevel.Warning:
                    Debug.LogWarning(message);
                    break;
                case LogLevel.Error:
                case LogLevel.Fatal:
                    Debug.LogError(message);
                    break;
            }
        }

        public void Flush() { }
        public void Close() { }
    }
}