using System.Collections.Generic;

namespace LFramework
{
    /// <summary>
    /// 远程日志上传的待发队列（D3 修复）。
    ///
    /// 存在理由：原实现的 <c>_pendingLogs</c> 是一个**无上限的 Queue**。
    /// 生产端（每条超阈值日志）远快于消费端（服务器不可达时原地退避重试），
    /// 于是队列只涨不落 —— 灾难场景（服务器挂了 + 业务疯狂报错）下，
    /// 日志系统自己会成为压死进程的最后一根稻草。
    /// 原代码虽有 <c>MAX_RETRIES</c> 丢弃机制，但它丢弃的是**整批 10 条**，
    /// 且只在连续失败超限时才触发，期间队列该涨多少还是涨多少。
    ///
    /// 本类的语义：
    /// - 超过 <see cref="Capacity"/> 时丢弃**最旧**的日志（保留最近的现场更有排查价值）；
    /// - 丢弃只计数不逐条打日志 —— 否则日志系统会变成自己的日志洪水来源；
    /// - 消费端的批次出队、失败重入队都走这里，保证顺序与计数一致。
    ///
    /// 之所以抽成独立类型：上限行为必须能被**确定性**地测试。
    /// 通过 <c>LogUploader</c> 去测就得起真实网络请求 + 退避循环，
    /// 测试既慢又不可控，还会往日志里灌真实错误。
    /// </summary>
    public sealed class LogUploadQueue
    {
        private readonly LinkedList<string> _queue = new LinkedList<string>();
        private readonly object _lock = new object();
        private int _capacity;
        private int _droppedCount;

        public LogUploadQueue(int capacity = LogUploader.DefaultMaxPendingLogs)
        {
            _capacity = capacity < 0 ? 0 : capacity;
        }

        /// <summary>
        /// 队列容量上限。&lt;= 0 表示不限制（保留旧行为，不建议）。
        /// 调小容量会立即把最旧的条目裁掉。
        /// </summary>
        public int Capacity
        {
            get { lock (_lock) return _capacity; }
            set
            {
                lock (_lock)
                {
                    _capacity = value < 0 ? 0 : value;
                    // 调小容量立即生效；0 表示不限制，不能当成"裁到 0 条"
                    TrimLocked(_capacity > 0 ? _capacity : -1);
                }
            }
        }

        /// <summary>当前待发条数。</summary>
        public int Count { get { lock (_lock) return _queue.Count; } }

        /// <summary>因超限被丢弃的累计条数。</summary>
        public int DroppedCount { get { lock (_lock) return _droppedCount; } }

        /// <summary>
        /// 入队一条待发日志。返回因超限被丢弃的条数（通常 0 或 1）。
        /// </summary>
        public int Enqueue(string item)
        {
            if (item == null) return 0;

            int dropped;
            lock (_lock)
            {
                // 给新条目腾一个位置；容量 <= 0 表示不限制，腾位置这一步直接跳过
                dropped = _capacity > 0 ? TrimLocked(_capacity - 1) : 0;
                _queue.AddLast(item);
            }
            return dropped;
        }

        /// <summary>
        /// 从队首取出至多 <paramref name="max"/> 条组成一批。队列为空时返回空列表（不返回 null）。
        /// </summary>
        public List<string> DequeueBatch(int max)
        {
            var batch = new List<string>();
            if (max <= 0) return batch;

            lock (_lock)
            {
                LinkedListNode<string> node = _queue.First;
                while (node != null && batch.Count < max)
                {
                    batch.Add(node.Value);
                    LinkedListNode<string> next = node.Next;
                    _queue.Remove(node);
                    node = next;
                }
            }
            return batch;
        }

        /// <summary>
        /// 把一批失败的日志放回**队首**（它们是当前最旧的，必须优先重发，否则会被后来的日志挤掉）。
        /// 重入队不受容量限制：批次本身有上限（见调用方 10 条），且丢失败批次等于丢现场。
        /// </summary>
        public void RequeueAtHead(IEnumerable<string> items)
        {
            if (items == null) return;

            lock (_lock)
            {
                // 倒序 Insert 到队首，恢复原有先后顺序
                var buffer = new List<string>(items);
                for (int i = buffer.Count - 1; i >= 0; i--)
                {
                    if (buffer[i] != null) _queue.AddFirst(buffer[i]);
                }
            }
        }

        /// <summary>清空队列（会话重置用）。丢弃计数一并归零。</summary>
        public void Clear()
        {
            lock (_lock)
            {
                _queue.Clear();
                _droppedCount = 0;
            }
        }

        /// <summary>把队列裁到 &lt;= <paramref name="targetCount"/> 条，返回丢弃数。传负数表示不裁剪。</summary>
        private int TrimLocked(int targetCount)
        {
            if (targetCount < 0) return 0;

            int dropped = 0;
            while (_queue.Count > targetCount)
            {
                _queue.RemoveFirst();
                dropped++;
            }
            _droppedCount += dropped;
            return dropped;
        }
    }
}
