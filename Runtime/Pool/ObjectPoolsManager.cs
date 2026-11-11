using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace LFramework
{
    /// <summary>
    /// 对象池管理器 - 优化版，使用字典实现 O(1) 查找
    /// </summary>
    public class ObjectPoolsManager : UnitySingleton<ObjectPoolsManager>, IModule, ITickable
    {
        //所有对象池的父物体
        private GameObject _poolsParent;

        //对象池父物体名称
        private const string POOLS_PARENT_NAME = "ObjectPools";

        //预制体到对象池的映射
        private readonly Dictionary<GameObject, ObjectPool> _prefabToPool = new Dictionary<GameObject, ObjectPool>();

        //游戏对象到其所属池的映射（O(1) 查找）
        private readonly Dictionary<GameObject, ObjectPool> _objectToPool = new Dictionary<GameObject, ObjectPool>();

        //自动裁剪间隔（秒），<=0 表示禁用
        public float AutoTrimInterval { get; set; } = 30f;

        //空池闲置超过该秒数将自动销毁
        public float EmptyPoolIdleTimeout { get; set; } = 60f;

        private float _lastTrimTime;

        private void CreatePoolsParentIfNull()
        {
            if (_poolsParent == null)
            {
                _poolsParent = new GameObject(POOLS_PARENT_NAME);
                DontDestroyOnLoad(_poolsParent);
            }
        }

        /// <summary>
        /// 从对象池获取游戏对象
        /// </summary>
        /// <param name="prefab">预制体</param>
        /// <param name="position">位置</param>
        /// <param name="rotation">旋转</param>
        /// <param name="parent">父物体</param>
        /// <returns>获取的游戏对象</returns>
        public GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent = null)
        {
            if (prefab == null)
            {
                LogManager.Error("[ObjectPoolsManager] Spawn failed: prefab is null");
                return null;
            }

            CreatePoolsParentIfNull();

            //查找或创建对象池
            ObjectPool pool = GetOrCreatePool(prefab);

            //从池中获取对象
            GameObject go = pool.Spawn(position, rotation, parent);

            //记录映射关系
            if (go != null)
            {
                _objectToPool[go] = pool;
            }

            return go;
        }

        /// <summary>
        /// 延迟回收游戏对象
        /// </summary>
        public void Despawn(GameObject go, float delayTime = 0)
        {
            if (go == null) return;

            if (delayTime > 0)
            {
                DespawnDelayedAsync(go, delayTime).Forget();
            }
            else
            {
                DespawnImmediate(go);
            }
        }

        /// <summary>
        /// 立即回收游戏对象。
        /// 返回是否**成功回收到池**（true = 进了所属池的空闲队列，可复用）。
        ///
        /// 打磨台账 F2：旧实现对"找不到所属池"的对象直接 <c>Destroy(go)</c> 且返回 void ——
        /// 调用方完全无从得知自己刚 despawn 的东西被销毁了，还以为是回收成功、
        /// 下次 Spawn 会复用。更糟的是这种对象通常来自别的池或根本不是池对象，
        /// 静默销毁会连带掐掉别人的活对象。
        /// 现在返回值 + 告警，让"没回收到池"这件事显形。
        /// </summary>
        public bool DespawnImmediate(GameObject go)
        {
            if (go == null) return false;

            //尝试从映射中查找对象池（O(1) 查找）
            if (_objectToPool.TryGetValue(go, out ObjectPool pool))
            {
                _objectToPool.Remove(go);
                pool.Despawn(go);
                return true;
            }

            //映射中没有，尝试通过遍历查找（降级方案）
            pool = FindPoolByUsedObject(go);
            if (pool != null)
            {
                pool.Despawn(go);
                return true;
            }

            // 找不到所属池：这不是本管理器的池对象（可能来自别的管理器、或根本没走池）。
            // 仍然销毁（保持原有行为，避免泄漏），但必须留痕 ——
            // 静默销毁会让"我明明 Despawn 了却还在漏"这类问题无从查起。
            LogManager.Warning(
                $"[ObjectPoolsManager] DespawnImmediate: 对象 {go.name} 不属于任何已登记的池，" +
                "已直接销毁（不会被复用）。若它来自其他池管理器或未通过 Spawn 创建，请改用对应的回收入口。");
            Destroy(go);
            return false;
        }

        /// <summary>
        /// 延迟回收（UniTask 版本，替代协程；go 若已被销毁，DespawnImmediate 内部判空安全返回）
        /// </summary>
        private async UniTaskVoid DespawnDelayedAsync(GameObject go, float delayTime)
        {
            await UniTask.Delay(TimeSpan.FromSeconds(delayTime));
            DespawnImmediate(go);
        }

        /// <summary>
        /// 通过游戏对象查找其所属的对象池
        /// </summary>
        private ObjectPool FindPoolByUsedObject(GameObject go)
        {
            foreach (var pool in _prefabToPool.Values)
            {
                //使用新的 Contains 方法进行 O(1) 查找
                if (pool.Contains(go))
                {
                    return pool;
                }
            }
            return null;
        }

        /// <summary>
        /// 获取或创建指定预制体对应的对象池
        /// </summary>
        private ObjectPool GetOrCreatePool(GameObject prefab)
        {
            //优先从映射中查找
            if (_prefabToPool.TryGetValue(prefab, out ObjectPool pool))
            {
                return pool;
            }

            //创建新的对象池
            GameObject poolGo = new GameObject($"Pool_{prefab.name}");
            poolGo.transform.SetParent(_poolsParent.transform);

            pool = poolGo.AddComponent<ObjectPool>();
            pool.prefab = prefab;

            //记录映射
            _prefabToPool[prefab] = pool;

            return pool;
        }

        /// <summary>
        /// 清空所有对象池
        /// </summary>
        public void ClearAll()
        {
            foreach (var pool in _prefabToPool.Values)
            {
                pool.Clear();
            }

            _prefabToPool.Clear();
            _objectToPool.Clear();
        }

        /// <summary>
        /// 获取指定预制体的对象池（如果存在）
        /// </summary>
        public ObjectPool GetPool(GameObject prefab)
        {
            if (prefab != null && _prefabToPool.TryGetValue(prefab, out ObjectPool pool))
            {
                return pool;
            }
            return null;
        }

        /// <summary>
        /// 获取对象池状态信息
        /// </summary>
        public string GetDebugInfo()
        {
            int totalPools = _prefabToPool.Count;
            int totalObjects = 0;

            foreach (var pool in _prefabToPool.Values)
            {
                totalObjects += pool.TotalCount;
            }

            return $"[ObjectPoolsManager] Pools: {totalPools}, Total Objects: {totalObjects}";
        }

        /// <summary>
        /// 裁剪所有池的空闲对象，清理已被外部销毁的对象，并销毁长时间空闲的空池
        /// </summary>
        public void TrimAll()
        {
            float now = Time.realtimeSinceStartup;
            List<GameObject> emptyPoolPrefabs = null;

            foreach (var kvp in _prefabToPool)
            {
                ObjectPool pool = kvp.Value;
                pool.Trim();
                pool.SweepDestroyed();

                // 空池且超过闲置阈值则销毁池本身
                if (pool.TotalCount == 0 && EmptyPoolIdleTimeout > 0
                    && now - pool.LastUsedTime > EmptyPoolIdleTimeout)
                {
                    emptyPoolPrefabs ??= new List<GameObject>();
                    emptyPoolPrefabs.Add(kvp.Key);
                }
            }

            // 清理已被外部 Destroy 的对象映射（Unity 假 null）
            if (_objectToPool.Count > 0)
            {
                List<GameObject> deadKeys = null;
                foreach (var kvp in _objectToPool)
                {
                    if (kvp.Key == null)
                    {
                        deadKeys ??= new List<GameObject>();
                        deadKeys.Add(kvp.Key);
                    }
                }
                if (deadKeys != null)
                {
                    foreach (var go in deadKeys) _objectToPool.Remove(go);
                }
            }

            if (emptyPoolPrefabs != null)
            {
                foreach (var prefab in emptyPoolPrefabs)
                {
                    if (_prefabToPool.TryGetValue(prefab, out ObjectPool pool))
                    {
                        if (pool != null && pool.gameObject != null)
                        {
                            Destroy(pool.gameObject);
                        }
                        _prefabToPool.Remove(prefab);
                    }
                }
            }
        }

        #region ITickable 实现

        /// <summary>周期裁剪由 ModuleManager 统一驱动（Phase 5 / 5.1：不再自挂 Update）。</summary>
        public void Tick(float deltaTime)
        {
            if (AutoTrimInterval <= 0) return;
            if (Time.realtimeSinceStartup - _lastTrimTime < AutoTrimInterval) return;
            _lastTrimTime = Time.realtimeSinceStartup;
            TrimAll();
        }

        #endregion

        #region IModule 实现

        public string DisplayName => GetType().Name;
        public UniTask InitializeAsync() => UniTask.CompletedTask;
        public void Dispose() => ClearAll();

        #endregion
    }
}
