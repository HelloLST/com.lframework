using System;
using System.Collections.Generic;
using UnityEngine;

namespace LFramework
{
    public static class EventCenter
    {
        private static readonly Dictionary<string, Dictionary<Type, Delegate>> _eventDict = new Dictionary<string, Dictionary<Type, Delegate>>();
        private static readonly object _lock = new object();

        private static Dictionary<Type, Delegate> GetOrCreateEventTypeDict(string eventName)
        {
            if (_eventDict.TryGetValue(eventName, out var typeDict)) return typeDict;
            typeDict = new Dictionary<Type, Delegate>();
            _eventDict.Add(eventName, typeDict);
            return typeDict;
        }

        private static void AddEventListenerInternal<T>(string eventName, T action) where T : Delegate
        {
            if (action == null) { Debug.LogError("EventCenter: Cannot add null event listener for " + eventName); return; }
            if (string.IsNullOrEmpty(eventName)) { Debug.LogError("EventCenter: Event name cannot be null or empty"); return; }
            lock (_lock)
            {
                var typeDict = GetOrCreateEventTypeDict(eventName);
                Type actionType = typeof(T);
                if (typeDict.TryGetValue(actionType, out var existingDelegate))
                {
                    var invocationList = existingDelegate.GetInvocationList();
                    if (Array.Exists(invocationList, d => d.Equals(action)))
                    {
                        Debug.LogWarning("EventCenter: Duplicate listener for " + eventName);
                        return;
                    }
                    typeDict[actionType] = Delegate.Combine(existingDelegate, action);
                }
                else { typeDict[actionType] = action; }
            }
        }

        private static void RemoveEventListenerInternal<T>(string eventName, T action) where T : Delegate
        {
            if (action == null || string.IsNullOrEmpty(eventName)) return;
            lock (_lock)
            {
                if (!_eventDict.TryGetValue(eventName, out var typeDict)) return;
                Type actionType = typeof(T);
                if (typeDict.TryGetValue(actionType, out var existingDelegate))
                {
                    var newDelegate = Delegate.Remove(existingDelegate, action);
                    if (newDelegate == null) { typeDict.Remove(actionType); if (typeDict.Count == 0) _eventDict.Remove(eventName); }
                    else { typeDict[actionType] = newDelegate; }
                }
            }
        }

        /// <summary>
        /// 在锁内取出指定委托类型的事件监听（无监听时返回 null，不告警）
        /// </summary>
        private static TDelegate GetDelegateLocked<TDelegate>(string eventName) where TDelegate : Delegate
        {
            lock (_lock)
            {
                if (!_eventDict.TryGetValue(eventName, out var typeDict)) return null;
                if (!typeDict.TryGetValue(typeof(TDelegate), out var eventDelegate)) return null;
                return (TDelegate)eventDelegate;
            }
        }

        private static void RemoveAllListenersInternal(string eventName)
        {
            if (string.IsNullOrEmpty(eventName)) return;
            lock (_lock) { _eventDict.Remove(eventName); }
        }

        public static void AddEventListener(string eventName, Action action) => AddEventListenerInternal(eventName, action);
        public static void AddEventListener<T>(string eventName, Action<T> action) => AddEventListenerInternal(eventName, action);
        public static void AddEventListener<T0,T1>(string eventName, Action<T0,T1> action) => AddEventListenerInternal(eventName, action);
        public static void AddEventListener<T0,T1,T2>(string eventName, Action<T0,T1,T2> action) => AddEventListenerInternal(eventName, action);
        public static void AddEventListener<T0,T1,T2,T3>(string eventName, Action<T0,T1,T2,T3> action) => AddEventListenerInternal(eventName, action);
        public static void AddEventListener<T0,T1,T2,T3,T4>(string eventName, Action<T0,T1,T2,T3,T4> action) => AddEventListenerInternal(eventName, action);

        /// <summary>
        /// 订阅事件并返回 IDisposable，using 块结束时自动退订，避免监听生命周期泄漏
        /// </summary>
        public static IDisposable Subscribe(string eventName, Action action)
        {
            AddEventListener(eventName, action);
            return new DisposableSubscription(() => RemoveListener(eventName, action));
        }

        public static IDisposable Subscribe<T>(string eventName, Action<T> action)
        {
            AddEventListener(eventName, action);
            return new DisposableSubscription(() => RemoveListener(eventName, action));
        }

        public static IDisposable Subscribe<T0,T1>(string eventName, Action<T0,T1> action)
        {
            AddEventListener(eventName, action);
            return new DisposableSubscription(() => RemoveListener(eventName, action));
        }

        public static IDisposable Subscribe<T0,T1,T2>(string eventName, Action<T0,T1,T2> action)
        {
            AddEventListener(eventName, action);
            return new DisposableSubscription(() => RemoveListener(eventName, action));
        }

        public static IDisposable Subscribe<T0,T1,T2,T3>(string eventName, Action<T0,T1,T2,T3> action)
        {
            AddEventListener(eventName, action);
            return new DisposableSubscription(() => RemoveListener(eventName, action));
        }

        public static IDisposable Subscribe<T0,T1,T2,T3,T4>(string eventName, Action<T0,T1,T2,T3,T4> action)
        {
            AddEventListener(eventName, action);
            return new DisposableSubscription(() => RemoveListener(eventName, action));
        }

        // 类型化派发：直接调用具体委托，避免 DynamicInvoke 的反射开销与参数装箱
        public static void TriggerEvent(string eventName)
        {
            GetDelegateLocked<Action>(eventName)?.Invoke();
        }

        public static void TriggerEvent<T>(string eventName, T arg0)
        {
            GetDelegateLocked<Action<T>>(eventName)?.Invoke(arg0);
        }

        public static void TriggerEvent<T0,T1>(string eventName, T0 arg0, T1 arg1)
        {
            GetDelegateLocked<Action<T0,T1>>(eventName)?.Invoke(arg0, arg1);
        }

        public static void TriggerEvent<T0,T1,T2>(string eventName, T0 arg0, T1 arg1, T2 arg2)
        {
            GetDelegateLocked<Action<T0,T1,T2>>(eventName)?.Invoke(arg0, arg1, arg2);
        }

        public static void TriggerEvent<T0,T1,T2,T3>(string eventName, T0 arg0, T1 arg1, T2 arg2, T3 arg3)
        {
            GetDelegateLocked<Action<T0,T1,T2,T3>>(eventName)?.Invoke(arg0, arg1, arg2, arg3);
        }

        public static void TriggerEvent<T0,T1,T2,T3,T4>(string eventName, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4)
        {
            GetDelegateLocked<Action<T0,T1,T2,T3,T4>>(eventName)?.Invoke(arg0, arg1, arg2, arg3, arg4);
        }

        public static void RemoveListener(string eventName, Action action) => RemoveEventListenerInternal(eventName, action);
        public static void RemoveListener<T>(string eventName, Action<T> action) => RemoveEventListenerInternal(eventName, action);
        public static void RemoveListener<T0,T1>(string eventName, Action<T0,T1> action) => RemoveEventListenerInternal(eventName, action);
        public static void RemoveListener<T0,T1,T2>(string eventName, Action<T0,T1,T2> action) => RemoveEventListenerInternal(eventName, action);
        public static void RemoveListener<T0,T1,T2,T3>(string eventName, Action<T0,T1,T2,T3> action) => RemoveEventListenerInternal(eventName, action);
        public static void RemoveListener<T0,T1,T2,T3,T4>(string eventName, Action<T0,T1,T2,T3,T4> action) => RemoveEventListenerInternal(eventName, action);
        public static void RemoveListener(string eventName) => RemoveAllListenersInternal(eventName);

        public static void ClearAllListeners() { lock (_lock) { _eventDict.Clear(); } }

        /// <summary>
        /// 关闭 Domain Reload 时静态字典会跨 Play 会话保留，进入 Play 时强制重置
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            ClearAllListeners();
        }

        public static int GetListenerCount(string eventName)
        {
            lock (_lock)
            {
                if (!_eventDict.TryGetValue(eventName, out var typeDict)) return 0;
                int total = 0;
                foreach (var kvp in typeDict) total += kvp.Value?.GetInvocationList().Length ?? 0;
                return total;
            }
        }

        public static string[] GetAllEventNames()
        {
            lock (_lock) { var names = new string[_eventDict.Keys.Count]; _eventDict.Keys.CopyTo(names, 0); return names; }
        }

        public static void DebugPrintEventSystem()
        {
            lock (_lock)
            {
                Debug.Log("=== EventCenter Debug Info ===");
                if (_eventDict.Count == 0) { Debug.Log("No events registered."); return; }
                foreach (var kvp in _eventDict)
                {
                    Debug.Log("Event: " + kvp.Key);
                    foreach (var tkvp in kvp.Value)
                    {
                        Debug.Log("  Type: " + tkvp.Key.Name + ", Listeners: " + (tkvp.Value?.GetInvocationList().Length ?? 0));
                    }
                }
                Debug.Log("=== End EventCenter Debug Info ===");
            }
        }

        /// <summary>
        /// 订阅令牌：Dispose 时执行退订，保证只退订一次
        /// </summary>
        private sealed class DisposableSubscription : IDisposable
        {
            private Action _unsubscribe;

            public DisposableSubscription(Action unsubscribe)
            {
                _unsubscribe = unsubscribe;
            }

            public void Dispose()
            {
                Action unsub = _unsubscribe;
                if (unsub == null) return;
                _unsubscribe = null;
                unsub();
            }
        }
    }
}
