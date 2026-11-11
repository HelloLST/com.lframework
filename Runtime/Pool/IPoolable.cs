using UnityEngine;

namespace LFramework
{
    /// <summary>
    /// 对象池接口 - 实现此接口的对象可以在回收时自动调用生命周期方法
    /// </summary>
    public interface IPoolable
    {
        /// <summary>对象从池中取出时调用</summary>
        void OnSpawn();
        /// <summary>对象回收进池时调用</summary>
        void OnDespawn();
    }

    /// <summary>
    /// 对象池项 - 包装游戏对象及其元数据
    /// </summary>
    public class PoolItem
    {
        public GameObject GameObject { get; }
        public ObjectPool ParentPool { get; }
        public bool IsActive => GameObject != null && GameObject.activeSelf;

        /// <summary>
        /// 缓存的 <see cref="IPoolable"/> 组件数组（F4 修复）。
        ///
        /// 为什么缓存：<c>GetComponents&lt;IPoolable&gt;()</c> 每次都会**新分配一个数组**，
        /// 即使对象上一个 IPoolable 都没有。Spawn/Despawn 是子弹、飘字、特效这类热路径，
        /// 每秒可能几百次，这个数组分配直接构成持续 GC 压力（表现为周期性的 GC 卡顿，
        /// 但不会报任何错，靠 Profiler 才能看出来）。
        ///
        /// 缓存的有效性前提：池对象的组件结构在实例化后不变 —— 这对池化对象是常态。
        /// 若运行期动态加/删了 <see cref="IPoolable"/> 组件，调用
        /// <see cref="InvalidatePoolableCache"/> 让缓存失效即可。
        /// </summary>
        private IPoolable[] _poolables;

        public PoolItem(GameObject go, ObjectPool pool)
        {
            GameObject = go;
            ParentPool = pool;
        }

        /// <summary>
        /// 让 <see cref="IPoolable"/> 组件缓存失效，下次 Spawn/Despawn 时重新收集。
        /// 运行期动态增删 IPoolable 组件后必须调用。
        /// </summary>
        public void InvalidatePoolableCache()
        {
            _poolables = null;
        }

        /// <summary>
        /// 取（并缓存）本对象上的 <see cref="IPoolable"/> 组件。
        /// GameObject 已被销毁、或确实没有组件时返回空数组而非 null，
        /// 调用方无需判空。
        /// </summary>
        private IPoolable[] GetPoolables()
        {
            if (_poolables != null) return _poolables;

            if (GameObject == null)
            {
                _poolables = System.Array.Empty<IPoolable>();
                return _poolables;
            }

            IPoolable[] found = GameObject.GetComponents<IPoolable>();
            _poolables = found ?? System.Array.Empty<IPoolable>();
            return _poolables;
        }

        /// <summary>
        /// 尝试调用 IPoolable 接口方法
        /// </summary>
        public void InvokeOnSpawn()
        {
            var poolables = GetPoolables();
            for (int i = 0; i < poolables.Length; i++)
            {
                IPoolable p = poolables[i];
                // 组件可能在两次调用之间被销毁（Unity 假 null），跳过而不是抛 NRE
                if (p == null) continue;

                try
                {
                    p.OnSpawn();
                }
                catch (System.Exception e)
                {
                    LogManager.Error($"[ObjectPool] OnSpawn error: {e.Message}", GameObject != null ? GameObject.name : "(已销毁)");
                }
            }
        }

        /// <summary>
        /// 尝试调用 IPoolable 接口方法
        /// </summary>
        public void InvokeOnDespawn()
        {
            var poolables = GetPoolables();
            for (int i = 0; i < poolables.Length; i++)
            {
                IPoolable p = poolables[i];
                if (p == null) continue;

                try
                {
                    p.OnDespawn();
                }
                catch (System.Exception e)
                {
                    LogManager.Error($"[ObjectPool] OnDespawn error: {e.Message}", GameObject != null ? GameObject.name : "(已销毁)");
                }
            }
        }
    }
}
