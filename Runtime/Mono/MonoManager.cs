using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

namespace LFramework
{
    /// <summary>
    /// Mono管理器 - 全工程逐帧派发中枢（Phase 5 / 5.2 加固）。
    /// 监听器使用 List 管理：重复添加去重、单个监听器异常隔离（不再连坐）、
    /// 倒序遍历允许监听器在回调中安全移除自己。
    /// 仍保留协程代理 API 供业务使用；框架内部代码一律走 UniTask / 监听器。
    /// </summary>
    public class MonoManager : UnitySingleton<MonoManager>, IModule
    {
        private readonly List<UnityAction> _updateListeners = new List<UnityAction>(32);
        private readonly List<UnityAction> _fixedUpdateListeners = new List<UnityAction>(8);
        private readonly List<UnityAction> _lateUpdateListeners = new List<UnityAction>(8);

        private void FixedUpdate()
        {
            InvokeSafe(_fixedUpdateListeners, "FixedUpdate");
        }

        private void Update()
        {
            InvokeSafe(_updateListeners, "Update");
        }

        private void LateUpdate()
        {
            InvokeSafe(_lateUpdateListeners, "LateUpdate");
        }

        private static void InvokeSafe(List<UnityAction> listeners, string phase)
        {
            // 倒序遍历：允许监听器在回调中移除自己
            for (int i = listeners.Count - 1; i >= 0; i--)
            {
                var call = listeners[i];
                if (call == null) { listeners.RemoveAt(i); continue; }
                try { call(); }
                catch (Exception e)
                {
                    // 单个监听器炸掉不应该连累其余监听器（修复前多播委托一炸全断）
                    LogManager.Error($"[MonoManager] {phase} 监听器异常（已隔离）:\n{e}");
                }
            }
        }

        #region 协程代理（业务便捷 API，框架内部不使用）

        /// <summary>让外部通过它来开启协程</summary>
        public new Coroutine StartCoroutine(IEnumerator routine) => base.StartCoroutine(routine);

        /// <summary>让外部通过它来停止协程</summary>
        public new void StopCoroutine(IEnumerator routine)
        {
            if (routine != null) base.StopCoroutine(routine);
        }

        /// <summary>让外部通过它来停止协程</summary>
        public new void StopCoroutine(Coroutine routine)
        {
            if (routine != null) base.StopCoroutine(routine);
        }

        /// <summary>让外部通过它来停止所有协程</summary>
        public new void StopAllCoroutines() => base.StopAllCoroutines();

        #endregion

        #region Update / FixedUpdate / LateUpdate 监听

        public void AddUpdateListener(UnityAction call)
        {
            if (call == null) return;
            if (_updateListeners.Contains(call)) { LogManager.Warning("[MonoManager] Update 监听器重复添加，已忽略"); return; }
            _updateListeners.Add(call);
        }

        public void RemoveUpdateListener(UnityAction call) => _updateListeners.Remove(call);

        public void RemoveAllUpdateListeners() => _updateListeners.Clear();

        public void AddFixedUpdateListener(UnityAction call)
        {
            if (call == null) return;
            if (_fixedUpdateListeners.Contains(call)) { LogManager.Warning("[MonoManager] FixedUpdate 监听器重复添加，已忽略"); return; }
            _fixedUpdateListeners.Add(call);
        }

        public void RemoveFixedUpdateListener(UnityAction call) => _fixedUpdateListeners.Remove(call);

        public void RemoveAllFixedUpdateListeners() => _fixedUpdateListeners.Clear();

        public void AddLateUpdateListener(UnityAction call)
        {
            if (call == null) return;
            if (_lateUpdateListeners.Contains(call)) { LogManager.Warning("[MonoManager] LateUpdate 监听器重复添加，已忽略"); return; }
            _lateUpdateListeners.Add(call);
        }

        public void RemoveLateUpdateListener(UnityAction call) => _lateUpdateListeners.Remove(call);

        public void RemoveAllLateUpdateListeners() => _lateUpdateListeners.Clear();

        /// <summary>移除所有事件</summary>
        public void RemoveAllListeners()
        {
            RemoveAllFixedUpdateListeners();
            RemoveAllUpdateListeners();
            RemoveAllLateUpdateListeners();
        }

        #endregion

        #region IModule 实现

        public string DisplayName => GetType().Name;
        public UniTask InitializeAsync() => UniTask.CompletedTask;
        public void Dispose() => RemoveAllListeners();

        #endregion
    }
}
