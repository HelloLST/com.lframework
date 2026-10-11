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

        public PoolItem(GameObject go, ObjectPool pool)
        {
            GameObject = go;
            ParentPool = pool;
        }

        /// <summary>
        /// 尝试调用 IPoolable 接口方法
        /// </summary>
        public void InvokeOnSpawn()
        {
            if (GameObject == null) return;

            var poolables = GameObject.GetComponents<IPoolable>();
            if (poolables != null)
            {
                foreach (var p in poolables)
                {
                    try
                    {
                        p.OnSpawn();
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"[ObjectPool] OnSpawn error: {e.Message}", GameObject);
                    }
                }
            }
        }

        /// <summary>
        /// 尝试调用 IPoolable 接口方法
        /// </summary>
        public void InvokeOnDespawn()
        {
            if (GameObject == null) return;

            var poolables = GameObject.GetComponents<IPoolable>();
            if (poolables != null)
            {
                foreach (var p in poolables)
                {
                    try
                    {
                        p.OnDespawn();
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"[ObjectPool] OnDespawn error: {e.Message}", GameObject);
                    }
                }
            }
        }
    }
}