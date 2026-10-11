using System;
using System.Collections.Generic;
using UnityEngine;

namespace LFramework.Architecture
{
    /// <summary>
    /// 运行时集合：用来追踪"当前场景里活着的某类对象"，替代 GameObject.Find / FindObjectOfType
    /// 这类每次调用都要遍历整个场景树的昂贵查找，也替代为了拿一个引用就搞出来的全局单例。
    ///
    /// 用法：对象自己在 OnEnable 时把自己登记进来，OnDisable 时摘掉（见 RuntimeSetRegistrar）。
    /// 谁需要就引用同一个资产，直接读 Items，O(1) 拿到，且完全不需要知道对方是谁。
    ///
    /// 安全红线：集合内容一律不序列化。把场景实例引用存进 SO 资产会造成
    /// 跨场景悬空引用和序列化报错，这是 SO 架构最常见的翻车点。
    /// </summary>
    public abstract class RuntimeSet<T> : ScriptableObject
    {
        // 刻意不加 [SerializeField]：运行时数据不落盘
        private readonly List<T> _items = new List<T>();

        public IReadOnlyList<T> Items => _items;

        public int Count => _items.Count;

        public event Action<T> OnItemAdded;
        public event Action<T> OnItemRemoved;

        /// <summary>集合数量发生任何变化时广播，UI 计数器订阅这个即可</summary>
        public event Action OnCountChanged;

        public void Add(T item)
        {
            if (item == null || _items.Contains(item)) return;
            _items.Add(item);
            OnItemAdded?.Invoke(item);
            OnCountChanged?.Invoke();
        }

        public void Remove(T item)
        {
            if (item == null || !_items.Remove(item)) return;
            OnItemRemoved?.Invoke(item);
            OnCountChanged?.Invoke();
        }

        public bool Contains(T item) => item != null && _items.Contains(item);

        /// <summary>取第一个成员，集合为空时返回默认值。适合"主角只有一个"这类场景。</summary>
        public T GetFirstOrDefault() => _items.Count > 0 ? _items[0] : default;

        public void Clear()
        {
            if (_items.Count == 0) return;
            _items.Clear();
            OnCountChanged?.Invoke();
        }

        // protected virtual 而非 private：避免子类定义同名方法时静默隐藏基类的清理逻辑
        protected virtual void OnEnable()
        {
            _items.Clear();
            OnItemAdded = null;
            OnItemRemoved = null;
            OnCountChanged = null;
        }

        protected virtual void OnDisable() => _items.Clear();
    }

    [CreateAssetMenu(menuName = "LFramework/Runtime Sets/Transform Set", fileName = "TransformRuntimeSet", order = 0)]
    public sealed class TransformRuntimeSet : RuntimeSet<Transform> { }

    [CreateAssetMenu(menuName = "LFramework/Runtime Sets/GameObject Set", fileName = "GameObjectRuntimeSet", order = 1)]
    public sealed class GameObjectRuntimeSet : RuntimeSet<GameObject> { }
}
