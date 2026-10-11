using System.Collections.Generic;
using UnityEngine;

namespace LFramework
{
    /// <summary>
    /// 优化后的对象池 - 使用字典实现 O(1) 查找，移除反射调用
    /// </summary>
    public class ObjectPool : MonoBehaviour
    {
        //对象池预制体
        public GameObject prefab;

        //对象池容量上限，-1 表示无限制
        public int capacity = -1;

        //正在使用的对象列表（已从池中取出）
        private readonly Dictionary<GameObject, PoolItem> _usedObjects = new Dictionary<GameObject, PoolItem>();

        //未被使用的对象队列（池中可用对象）
        private readonly Queue<PoolItem> _unusedObjects = new Queue<PoolItem>();

        //最后一次 Spawn 或 Despawn 的时间，用于 Trim 判断空池闲置时长
        public float LastUsedTime { get; private set; }

        //对象总数
        public int TotalCount => _usedObjects.Count + _unusedObjects.Count;

        //正在使用的对象数量
        public int UsedCount => _usedObjects.Count;

        //未被使用的对象数量
        public int UnusedCount => _unusedObjects.Count;

        /// <summary>
        /// 从对象池获取一个对象
        /// </summary>
        /// <param name="position">位置</param>
        /// <param name="rotation">旋转</param>
        /// <param name="parent">父物体</param>
        /// <returns>获取的游戏对象</returns>
        public GameObject Spawn(Vector3 position, Quaternion rotation, Transform parent = null)
        {
            PoolItem item;

            //优先从池中获取
            if (_unusedObjects.Count > 0)
            {
                item = _unusedObjects.Dequeue();
            }
            else
            {
                //池中无对象，创建新的
                GameObject go = Instantiate(prefab, position, rotation, parent);
                item = new PoolItem(go, this);
            }

            //设置位置和父物体
            item.GameObject.transform.localPosition = position;
            item.GameObject.transform.localRotation = rotation;
            item.GameObject.transform.SetParent(parent, false);
            item.GameObject.SetActive(true);

            //添加到已使用字典（O(1) 查找）；防止同一对象被重复 Spawn 时抛异常
            if (_usedObjects.ContainsKey(item.GameObject))
            {
                Debug.LogError($"[ObjectPool] Spawn failed: 对象 {item.GameObject.name} 已在使用中，请先 Despawn");
            }
            else
            {
                _usedObjects.Add(item.GameObject, item);
                //调用 OnSpawn 生命周期
                item.InvokeOnSpawn();
            }

            LastUsedTime = Time.realtimeSinceStartup;
            return item.GameObject;
        }

        /// <summary>
        /// 回收游戏对象到对象池
        /// </summary>
        /// <param name="go">要回收的游戏对象</param>
        public void Despawn(GameObject go)
        {
            if (go == null) return;

            //从已使用字典中查找（O(1) 查找）
            if (!_usedObjects.TryGetValue(go, out PoolItem item))
            {
                //不在当前池中，可能是其他池管理的
                return;
            }

            //移除已使用字典
            _usedObjects.Remove(go);

            //检查容量，超出容量则销毁而非回收
            if (capacity >= 0 && _unusedObjects.Count >= capacity)
            {
                //销毁超出的对象
                Destroy(go);
                return;
            }

            //回收进池
            go.SetActive(false);
            go.transform.SetParent(transform, false);
            _unusedObjects.Enqueue(item);

            //调用 OnDespawn 生命周期
            item.InvokeOnDespawn();

            LastUsedTime = Time.realtimeSinceStartup;
        }

        /// <summary>
        /// 裁剪空闲对象到指定数量（默认按 capacity；capacity < 0 时不裁剪）
        /// </summary>
        /// <param name="targetIdleCount">目标空闲数量；-1 表示按 capacity 裁剪</param>
        public void Trim(int targetIdleCount = -1)
        {
            int target = targetIdleCount;
            if (target < 0)
            {
                if (capacity < 0) return;
                target = capacity;
            }

            while (_unusedObjects.Count > target)
            {
                var item = _unusedObjects.Dequeue();
                if (item.GameObject != null)
                {
                    Destroy(item.GameObject);
                }
            }
        }

        /// <summary>
        /// 回收所有正在使用的对象
        /// </summary>
        public void DespawnAll()
        {
            //复制键集合，避免迭代中修改字典
            var keys = new List<GameObject>(_usedObjects.Keys);

            foreach (var key in keys)
            {
                if (key != null)
                {
                    Despawn(key);
                }
            }
        }

        /// <summary>
        /// 清理已被外部 Destroy 的对象（Unity 假 null），返回清理数量
        /// 这类对象无法通过 Despawn 正常回收，需周期扫描移除，避免字典无限膨胀
        /// </summary>
        public int SweepDestroyed()
        {
            if (_usedObjects.Count == 0) return 0;

            List<GameObject> dead = null;
            foreach (var kvp in _usedObjects)
            {
                if (kvp.Key == null)
                {
                    dead ??= new List<GameObject>();
                    dead.Add(kvp.Key);
                }
            }

            if (dead == null) return 0;
            foreach (var go in dead) _usedObjects.Remove(go);
            return dead.Count;
        }

        /// <summary>
        /// 预加载指定数量的对象到池中
        /// </summary>
        /// <param name="count">预加载数量</param>
        public void Preload(int count = 1)
        {
            if (prefab == null || count <= 0) return;

            for (int i = 0; i < count; i++)
            {
                GameObject go = Instantiate(prefab, Vector3.zero, Quaternion.identity);
                go.SetActive(false);
                go.transform.SetParent(transform, false);
                go.name = prefab.name;

                _unusedObjects.Enqueue(new PoolItem(go, this));
            }
        }

        /// <summary>
        /// 清空对象池，销毁所有对象
        /// </summary>
        public void Clear()
        {
            //销毁所有正在使用的对象
            foreach (var item in _usedObjects.Values)
            {
                if (item.GameObject != null)
                {
                    Destroy(item.GameObject);
                }
            }
            _usedObjects.Clear();

            //销毁所有未使用的对象
            while (_unusedObjects.Count > 0)
            {
                var item = _unusedObjects.Dequeue();
                if (item.GameObject != null)
                {
                    Destroy(item.GameObject);
                }
            }
        }

        /// <summary>
        /// 检查游戏对象是否属于此池
        /// </summary>
        public bool Contains(GameObject go)
        {
            return go != null && _usedObjects.ContainsKey(go);
        }

        private void OnDestroy()
        {
            Clear();
        }
    }
}
