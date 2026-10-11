using UnityEngine;
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace LFramework
{
    /// <summary>
    /// 计时器数据
    /// </summary>
    public class TimerData
    {
        //持续时间
        private float duration;

        //是否循环
        private bool isLoop;

        //是否忽略时间缩放
        private bool ignoreTimeScale;

        //注册时的回调
        private Action onRegister;

        //更新时的回调(每帧)
        private Action<float> onUpdate;

        //完成时的回调
        private Action onComplete;

        //是否完成(到达目标时间)
        private bool isDone;

        //开始计时的时间(距游戏开始运行)
        private float beginTime;

        //上一次更新的时间(距游戏开始运行)
        private float lastUpdateTime;

        //暂停的总时长
        private float pauseTotalTime;

        //是否暂停
        private bool isPaused;

        //是否被销毁
        private bool isDisposed;

        //是否完成
        public bool IsCompleted
        {
            get
            {
                return isDone || isDisposed;
            }
        }

        /// <summary>
        /// 重置状态，准备复用（由 TimerMgr 池化时调用）
        /// </summary>
        internal void Reset()
        {
            duration = 0;
            isLoop = false;
            ignoreTimeScale = false;
            onRegister = null;
            onUpdate = null;
            onComplete = null;
            isDone = false;
            beginTime = 0;
            lastUpdateTime = 0;
            pauseTotalTime = 0;
            isPaused = false;
            isDisposed = false;
        }

        /// <summary>
        /// 注册计时器
        /// </summary>
        public void Register(float duration, bool isLoop, bool ignoreTimeScale,
            Action onRegister, Action onComplete, Action<float> onUpdate)
        {
            this.duration = duration;
            this.isLoop = isLoop;
            this.ignoreTimeScale = ignoreTimeScale;
            this.onRegister = onRegister;
            this.onComplete = onComplete;
            this.onUpdate = onUpdate;

            onRegister?.Invoke();

            pauseTotalTime = 0;
            beginTime = GetWorldTime();
            lastUpdateTime = beginTime;
        }

        /// <summary>
        /// 暂停计时器
        /// </summary>
        public void Pause()
        {
            if (isPaused
                || IsCompleted)
            {
                return;
            }

            isPaused = true;
        }

        /// <summary>
        /// 恢复计时器
        /// </summary>
        public void Resume()
        {
            if (!isPaused
                || IsCompleted)
            {
                return;
            }

            isPaused = false;
        }

        /// <summary>
        /// 销毁计时器
        /// </summary>
        public void Dispose()
        {
            if (IsCompleted)
            {
                return;
            }

            isDisposed = true;
        }

        /// <summary>
        /// 更新计时器
        /// </summary>
        public void Update()
        {
            if (IsCompleted)
            {
                return;
            }

            if (isPaused)
            {
                pauseTotalTime += GetDeltaTime();
                lastUpdateTime = GetWorldTime();
                return;
            }

            onUpdate?.Invoke(GetTimeLeft());
            lastUpdateTime = GetWorldTime();
            if (GetWorldTime() >= GetTargetTime())
            {
                onComplete?.Invoke();

                if (isLoop)
                {
                    beginTime = GetWorldTime();
                    pauseTotalTime = 0;
                }
                else
                {
                    isDone = true;
                }
            }
        }

        /// <summary>
        /// 得到剩余时间
        /// </summary>
        public float GetTimeLeft()
        {
            if (IsCompleted
                || GetWorldTime() >= GetTargetTime())
            {
                return 0;
            }

            float timeLeft = GetTargetTime() - GetWorldTime();
            return timeLeft;
        }

        /// <summary>
        /// 得到剩余时间比例
        /// </summary>
        public float GetTimeLeftRatio()
        {
            if (IsCompleted
                || GetWorldTime() >= GetTargetTime())
            {
                return 1;
            }

            float timeLeftRatio = GetTimeLeft() / duration;
            return timeLeftRatio;
        }

        /// <summary>
        /// 得到时间(距游戏开始运行)
        /// </summary>
        private float GetWorldTime()
        {
            float worldTime = ignoreTimeScale
                ? Time.realtimeSinceStartup
                : Time.time;
            return worldTime;
        }

        /// <summary>
        /// 得到目标时间(距游戏开始运行)
        /// </summary>
        private float GetTargetTime()
        {
            float targetTime = beginTime + duration + pauseTotalTime;
            return targetTime;
        }

        /// <summary>
        /// 得到增量时间
        /// </summary>
        private float GetDeltaTime()
        {
            float deltaTime = GetWorldTime() - lastUpdateTime;
            return deltaTime;
        }
    }

    /// <summary>
    /// 计时器管理器
    /// </summary>
    public class TimerMgr : UnitySingleton<TimerMgr>, IModule
    {
        //计时器列表
        private List<TimerData> m_TimerList = new List<TimerData>();

        //计时器列表(先缓存所有计时器)
        private List<TimerData> m_TimerListToAdd = new List<TimerData>();

        //空闲 TimerData 池，复用以避免 GC
        private Queue<TimerData> m_TimerPool = new Queue<TimerData>();

        private TimerData AcquireTimer()
        {
            if (m_TimerPool.Count > 0)
            {
                var t = m_TimerPool.Dequeue();
                t.Reset();
                return t;
            }
            return new TimerData();
        }

        /// <summary>
        /// 注册计时器
        /// </summary>
        public TimerData Register(float duration, bool isLoop = false, bool ignoreTimeScale = false
            , Action onRegister = null, Action onComplete = null, Action<float> onUpdate = null)
        {
            TimerData data = AcquireTimer();
            data.Register(duration, isLoop, ignoreTimeScale, onRegister, onComplete, onUpdate);
            m_TimerListToAdd.Add(data);
            return data;
        }

        /// <summary>
        /// 注册计时器
        /// </summary>
        public TimerData Register(float duration, Action onComplete = null)
        {
            TimerData data = AcquireTimer();
            data.Register(duration, false, false, null, onComplete, null);
            m_TimerListToAdd.Add(data);
            return data;
        }

        /// <summary>
        /// 注册常用计时器
        /// </summary>
        public TimerData Register(float duration, Action<float> onUpdate = null, Action onComplete = null, Action onRegister = null)
        {
            TimerData data = AcquireTimer();
            data.Register(duration, false, false, onRegister, onComplete, onUpdate);
            m_TimerListToAdd.Add(data);
            return data;
        }

        /// <summary>
        /// 销毁所有计时器
        /// </summary>
        public void DisposeAll()
        {
            foreach (var timer in m_TimerList)
            {
                timer.Dispose();
                m_TimerPool.Enqueue(timer);
            }
            foreach (var timer in m_TimerListToAdd)
            {
                timer.Dispose();
                m_TimerPool.Enqueue(timer);
            }
            m_TimerList.Clear();
            m_TimerListToAdd.Clear();
        }

        /// <summary>
        /// 暂停所有计时器
        /// </summary>
        public void PauseAll()
        {
            foreach (var timer in m_TimerList)
            {
                timer.Pause();
            }
        }

        /// <summary>
        /// 恢复所有计时器
        /// </summary>
        public void ResumeAll()
        {
            foreach (var timer in m_TimerList)
            {
                timer.Resume();
            }
        }

        /// <summary>
        /// 更新所有计时器（swap-remove + 回收到空闲池）
        /// </summary>
        public void UpdateAll()
        {
            if (m_TimerListToAdd.Count > 0)
            {
                for (int i = 0; i < m_TimerListToAdd.Count; i++)
                {
                    m_TimerList.Add(m_TimerListToAdd[i]);
                }
                m_TimerListToAdd.Clear();
            }

            // swap-remove：从末尾开始遍历，完成的与末尾交换并 RemoveAt(末尾)
            for (int i = m_TimerList.Count - 1; i >= 0; i--)
            {
                var timer = m_TimerList[i];
                timer.Update();
                if (timer.IsCompleted)
                {
                    int last = m_TimerList.Count - 1;
                    if (i != last)
                    {
                        m_TimerList[i] = m_TimerList[last];
                    }
                    m_TimerList.RemoveAt(last);
                    m_TimerPool.Enqueue(timer);
                }
            }
        }

        private void Update()
        {
            UpdateAll();
        }

        /// <summary>
        /// 延迟指定秒数（UniTask 版本，作为协程替代）
        /// </summary>
        public static UniTask Delay(float seconds, bool ignoreTimeScale = false, CancellationToken cancellationToken = default)
        {
            return UniTask.Delay(
                TimeSpan.FromSeconds(seconds),
                ignoreTimeScale,
                PlayerLoopTiming.Update,
                cancellationToken);
        }

        #region IModule 实现

        public string DisplayName => GetType().Name;
        public UniTask InitializeAsync() => UniTask.CompletedTask;
        public void Tick(float deltaTime) { }
        public void Dispose() => DisposeAll();

        #endregion
    }
}
