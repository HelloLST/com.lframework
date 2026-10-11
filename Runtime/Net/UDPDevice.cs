using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace LFramework
{
    /// <summary>
    /// UDP 网络通信设备（支持多线程安全）
    /// 注意：DataReceived 事件现在会在主线程触发，可以安全地访问 Unity API
    /// </summary>
    public class UDPDevice : IDisposable
    {
        /// <summary>
        /// 数据接收事件（在主线程触发，可安全访问 Unity API）
        /// </summary>
        public event Action<string> DataReceived;

        // 接收队列滚动窗口上限：无人消费时丢弃最旧数据，防止无限增长
        private const int MAX_RECEIVE_QUEUE_SIZE = 10000;

        private volatile bool _isRunning = true;
        private volatile bool _isDisposed = false;
        private UdpClient _client;
        private Thread _receiveThread;
        private int _port;

        // 后台线程接收的数据队列（线程安全）
        private ConcurrentQueue<string> _receiveDataQueue = new ConcurrentQueue<string>();

        // 主线程待触发事件的数据队列
        private ConcurrentQueue<string> _eventTriggerQueue = new ConcurrentQueue<string>();

        // 用于安全检测主线程队列的锁
        private readonly object _processLock = new object();

        /// <summary>
        /// UDPDevice 是否正在运行
        /// </summary>
        public bool IsRunning => _isRunning && !_isDisposed;

        /// <summary>
        /// 实例化 UDP 设备并开始监听指定端口
        /// </summary>
        /// <param name="port">监听端口号</param>
        /// <exception cref="SocketException">端口被占用或网络异常时抛出</exception>
        public UDPDevice(int port)
        {
            _port = port;

            try
            {
                _client = new UdpClient(new IPEndPoint(IPAddress.Any, port));

                _receiveThread = new Thread(ReceiveLoop)
                {
                    IsBackground = true,
                    Name = $"UDP_Receive_Thread_{port}"
                };
                _receiveThread.Start();
            }
            catch (SocketException e)
            {
                _isRunning = false;
                LogManager.Error($"UDPDevice: Failed to bind port {port}. Error: {e.Message}");
                throw;
            }
            catch (Exception e)
            {
                _isRunning = false;
                LogManager.Error($"UDPDevice: Failed to initialize. Error: {e.Message}");
                throw;
            }
        }

        /// <summary>
        /// UDP 接收线程（后台线程）
        /// </summary>
        private void ReceiveLoop()
        {
            IPEndPoint endpoint = new IPEndPoint(IPAddress.Any, 0);

            try
            {
                while (_isRunning && !_isDisposed && _client != null)
                {
                    try
                    {
                        // 阻塞接收数据（设置超时以支持快速退出）
                        byte[] receiveBytes = _client.Receive(ref endpoint);

                        if (receiveBytes != null && receiveBytes.Length > 0)
                        {
                            string message = Encoding.UTF8.GetString(receiveBytes);

                            // 将数据放入两个队列：
                            // 1. _receiveDataQueue - 供手动 GetData 获取
                            // 2. _eventTriggerQueue - 供主线程触发事件
                            EnqueueWithLimit(_receiveDataQueue, message, MAX_RECEIVE_QUEUE_SIZE);
                            EnqueueWithLimit(_eventTriggerQueue, message, MAX_RECEIVE_QUEUE_SIZE);
                        }
                    }
                    catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
                    {
                        // 超时是正常的，继续循环检查退出标志
                        continue;
                    }
                    catch (ObjectDisposedException)
                    {
                        // 对象已释放，正常退出
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                if (_isRunning && !_isDisposed)
                {
                    LogManager.Error($"UDPDevice: Receive thread error. {e.Message}");
                }
            }
            finally
            {
                _isRunning = false;
            }
        }

        /// <summary>
        /// 入队并做滚动窗口保护：队列达到上限时丢弃最旧一条，避免无人消费时无限增长
        /// </summary>
        private static void EnqueueWithLimit(ConcurrentQueue<string> queue, string message, int maxSize)
        {
            if (maxSize > 0 && queue.Count >= maxSize)
            {
                queue.TryDequeue(out _);
            }
            queue.Enqueue(message);
        }

        /// <summary>
        /// 在主线程每帧调用，处理待触发事件的数据
        /// </summary>
        /// <remarks>
        /// 必须在主线程的 Update 中调用此方法，才能确保 DataReceived 事件在主线程触发
        /// </remarks>
        public void Update()
        {
            if (_isDisposed)
                return;

            lock (_processLock)
            {
                while (_eventTriggerQueue.TryDequeue(out string data))
                {
                    try
                    {
                        DataReceived?.Invoke(data);

                        // 如果事件被触发但回调中没有处理，需要确保队列继续处理
                        if (_eventTriggerQueue.Count > 1000)
                        {
                            LogManager.Warning($"UDPDevice: Event queue size is large ({_eventTriggerQueue.Count}). " +
                                $"Consider consuming events faster.");
                        }
                    }
                    catch (Exception e)
                    {
                        LogManager.Error($"UDPDevice: Error in DataReceived callback. {e.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// 手动获取接收到的数据（非事件模式）
        /// </summary>
        /// <returns>接收到的数据，无数据时返回 null</returns>
        public string GetData()
        {
            _receiveDataQueue.TryDequeue(out string data);
            return data;
        }

        /// <summary>
        /// 获取所有已接收的数据（迭代器模式）
        /// </summary>
        /// <returns>目前队列中的所有数据</returns>
        public IEnumerable<string> GetDataEnumerable()
        {
            while (_receiveDataQueue.TryDequeue(out string data))
            {
                yield return data;
            }
        }

        /// <summary>
        /// 发送 UDP 消息到本地回环
        /// </summary>
        /// <param name="targetPort">目标端口</param>
        /// <param name="message">消息内容</param>
        /// <returns>是否发送成功</returns>
        public bool SendUdp(int targetPort, string message)
        {
            return SendUdp(targetPort, "127.0.0.1", message);
        }

        /// <summary>
        /// 发送 UDP 消息到指定 IP 地址
        /// </summary>
        /// <param name="targetPort">目标端口</param>
        /// <param name="targetIP">目标 IP 地址</param>
        /// <param name="message">消息内容</param>
        /// <returns>是否发送成功</returns>
        public bool SendUdp(int targetPort, string targetIP, string message)
        {
            if (_isDisposed || _client == null)
            {
                LogManager.Warning("UDPDevice: Cannot send, device is not initialized or has been disposed.");
                return false;
            }

            try
            {
                IPEndPoint endpoint = new IPEndPoint(IPAddress.Parse(targetIP), targetPort);
                byte[] dataBytes = Encoding.UTF8.GetBytes(message);
                _client.Send(dataBytes, dataBytes.Length, endpoint);
                return true;
            }
            catch (FormatException)
            {
                LogManager.Error($"UDPDevice: Invalid IP address format: {targetIP}");
                return false;
            }
            catch (Exception e)
            {
                LogManager.Error($"UDPDevice: Failed to send UDP message. {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// 关闭 UDP 设备并释放资源
        /// </summary>
        public void UDPClose()
        {
            Dispose();
        }

        /// <summary>
        /// 释放 UDP 资源（实现 IDisposable）
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            _isRunning = false;

            try
            {
                if (_client != null)
                {
                    // 直接向本机端口发送停止消息以解除阻塞的 Receive 调用
                    // 注意：不能走 SendUdp()，此时 _isDisposed 已为 true，SendUdp 会直接拒绝
                    try
                    {
                        byte[] signal = Encoding.UTF8.GetBytes("SHUTDOWN_SIGNAL");
                        _client.Send(signal, signal.Length, new IPEndPoint(IPAddress.Loopback, _port));
                    }
                    catch { /* 关闭时忽略发送错误 */ }

                    // 关闭客户端（也会让阻塞的 Receive 抛 ObjectDisposedException 退出）
                    try
                    {
                        _client.Close();
                    }
                    catch { /* Close 可能抛出异常，忽略 */ }
                }

                // 等待接收线程正常退出（使用 Join 而非 Abort）
                if (_receiveThread != null && _receiveThread.IsAlive)
                {
                    if (!_receiveThread.Join(500))
                    {
                        LogManager.Warning("UDPDevice: Receive thread did not exit gracefully within 500ms. " +
                            "This may indicate the receive operation is blocked.");
                    }
                }
            }
            finally
            {
                _client = null;
                _receiveThread = null;

                // ConcurrentQueue 没有 Clear() 方法，需要手动清空
                while (_receiveDataQueue.TryDequeue(out _)) { }
                while (_eventTriggerQueue.TryDequeue(out _)) { }

                DataReceived = null;
            }
        }
    }
}
