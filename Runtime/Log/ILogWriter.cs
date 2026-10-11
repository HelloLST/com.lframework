// ILogWriter.cs
// 日志写入器接口

namespace LFramework
{
    /// <summary>
    /// 日志写入器接口
    /// </summary>
    public interface ILogWriter
    {
        /// <summary>
        /// 写入日志
        /// </summary>
        void Write(LogEntry entry);

        /// <summary>
        /// 刷新缓冲区
        /// </summary>
        void Flush();

        /// <summary>
        /// 关闭写入器
        /// </summary>
        void Close();

        /// <summary>
        /// 最低日志级别
        /// </summary>
        LogLevel MinLevel { get; set; }
    }
}