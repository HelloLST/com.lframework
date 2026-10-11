using System.Collections;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

namespace LFramework
{
    /// <summary>
    /// Mono管理器 - 改造为直接继承 MonoBehaviour 的 UnitySingleton
    /// </summary>
    public class MonoManager : UnitySingleton<MonoManager>, IModule
    {
        private event UnityAction updateEvent;
        private event UnityAction fixedUpdateEvent;
        private event UnityAction lateUpdateEvent;

        private void FixedUpdate()
        {
            fixedUpdateEvent?.Invoke();
        }

        private void Update()
        {
            updateEvent?.Invoke();
        }

        private void LateUpdate()
        {
            lateUpdateEvent?.Invoke();
        }

        /// <summary>
        /// 让外部通过它来开启协程
        /// </summary>
        public new Coroutine StartCoroutine(IEnumerator routine)
        {
            return base.StartCoroutine(routine);
        }

        /// <summary>
        /// 让外部通过它来停止协程
        /// </summary>
        public new void StopCoroutine(IEnumerator routine)
        {
            if (routine != null)
                base.StopCoroutine(routine);
        }

        /// <summary>
        /// 让外部通过它来停止协程
        /// </summary>
        public new void StopCoroutine(Coroutine routine)
        {
            if (routine != null)
                base.StopCoroutine(routine);
        }

        /// <summary>
        /// 让外部通过它来停止所有协程
        /// </summary>
        public new void StopAllCoroutines()
        {
            base.StopAllCoroutines();
        }

        /// <summary>
        /// 添加Update事件
        /// </summary>
        public void AddUpdateListener(UnityAction call)
        {
            updateEvent += call;
        }

        /// <summary>
        /// 移除Update事件
        /// </summary>
        public void RemoveUpdateListener(UnityAction call)
        {
            updateEvent -= call;
        }

        /// <summary>
        /// 移除所有Update事件
        /// </summary>
        public void RemoveAllUpdateListeners()
        {
            updateEvent = null;
        }

        /// <summary>
        /// 添加FixedUpdate事件
        /// </summary>
        public void AddFixedUpdateListener(UnityAction call)
        {
            fixedUpdateEvent += call;
        }

        /// <summary>
        /// 移除FixedUpdate事件
        /// </summary>
        public void RemoveFixedUpdateListener(UnityAction call)
        {
            fixedUpdateEvent -= call;
        }

        /// <summary>
        /// 移除所有FixedUpdate事件
        /// </summary>
        public void RemoveAllFixedUpdateListeners()
        {
            fixedUpdateEvent = null;
        }

        /// <summary>
        /// 添加LateUpdate事件
        /// </summary>
        public void AddLateUpdateListener(UnityAction call)
        {
            lateUpdateEvent += call;
        }

        /// <summary>
        /// 移除LateUpdate事件
        /// </summary>
        public void RemoveLateUpdateListener(UnityAction call)
        {
            lateUpdateEvent -= call;
        }

        /// <summary>
        /// 移除所有LateUpdate事件
        /// </summary>
        public void RemoveAllLateUpdateListeners()
        {
            lateUpdateEvent = null;
        }

        /// <summary>
        /// 移除所有事件
        /// </summary>
        public void RemoveAllListeners()
        {
            RemoveAllFixedUpdateListeners();
            RemoveAllUpdateListeners();
            RemoveAllLateUpdateListeners();
        }

        #region IModule 实现

        public string DisplayName => GetType().Name;
        public UniTask InitializeAsync() => UniTask.CompletedTask;
        public void Tick(float deltaTime) { }
        public void Dispose() => RemoveAllListeners();

        #endregion

    }
}