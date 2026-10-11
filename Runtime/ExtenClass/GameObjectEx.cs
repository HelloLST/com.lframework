using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;

namespace LFramework
{
    public static class GameObjectEx
    {
        public static void Show(this GameObject obj)
        {
            obj.SetActive(true);
        }

        public static void Hide(this GameObject obj)
        {
            obj.SetActive(false);
        }

        /// <summary>
        /// 获取组件 若没有就添加该组件并返回
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="origin"></param>
        /// <returns></returns>
        public static T GetOrAddComponent<T>(this GameObject origin) where T : Component
        {
            T componment = origin.GetComponent<T>();
            if (componment == null)
            {
                componment = origin.AddComponent<T>();
            }
            return componment;
        }

        /// <summary>
        /// 查找该物体下面的孩子[包含物体对象本身]
        /// </summary>
        /// <param name="pTran"></param>
        /// <param name="name"></param>
        /// <param name="includeInactive">是否包含未激活的物体</param>
        /// <returns></returns>
        public static GameObject FindChildX(this GameObject pTran, string name, bool includeInactive = true)
        {
            var childs = pTran.GetComponentsInChildren<Transform>(includeInactive);
            var max = childs.Length;

            for (int idx = 0; idx < max; idx++)
            {
                if (childs[idx] == null) continue;
                if (childs[idx].name == name)
                {
                    return childs[idx].gameObject;
                }
            }
            return null;
        }

        /// <summary>
        /// 获取该物体下所有的组件，不包括本身
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="go"></param>
        /// <param name="includeInactive">是否包含未激活的物体</param>
        /// <returns></returns>
        public static T[] GetComponentInRealChildren<T>(this GameObject go, [System.ComponentModel.DefaultValue("true")] bool includeInactive = true) where T : Component
        {
            var components = go.GetComponentsInChildren(typeof(T), includeInactive);
            var max = components.Length;
            List<T> newList = new List<T>();
            for (int idx = 0; idx < max; idx++)
            {
                if (components[idx].gameObject == go)
                    continue;
                newList.Add(components[idx] as T);
            }
            return newList.ToArray();
        }

        public static void SetParentAndPos(this GameObject pObj, Vector3 pLocalPos, Transform pParent = null)
        {
            SetParentAndTrans(pObj, pParent);
            pObj.transform.transform.localPosition = pLocalPos;
        }

        public static void SetParentAndRot(this GameObject pObj, Vector3 pLocalRot, Transform pParent = null)
        {
            SetParentAndTrans(pObj, pParent);
            pObj.transform.transform.localRotation = Quaternion.Euler(pLocalRot);
        }

        public static void SetParentAndScale(this GameObject pObj, Vector3 pLocalScale, Transform pParent = null)
        {
            SetParentAndTrans(pObj, pParent);
            pObj.transform.transform.localScale = pLocalScale;
        }

        public static void SetParentAndTrans(this GameObject pObj, Transform pParent = null)
        {
            if (pParent != null)
            {
                pObj.transform.transform.parent = pParent;
            }
        }

        /// <summary>
        /// 将游戏对象回收进对象池（延迟）
        /// </summary>
        public static void Despawn(this GameObject go, float delayTime = 0)
        {
            ObjectPoolsManager.Instance.Despawn(go, delayTime);
        }

        /// <summary>
        /// 立即将游戏对象回收进对象池
        /// </summary>
        public static void DespawnImmediate(this GameObject go)
        {
            ObjectPoolsManager.Instance.DespawnImmediate(go);
        }

        /// <summary>
        /// 从对象池获取游戏对象
        /// </summary>
        public static GameObject Spawn(this GameObject prefab, Vector3 position, Quaternion rotation, Transform parent = null)
        {
            return ObjectPoolsManager.Instance.Spawn(prefab, position, rotation, parent);
        }

        /// <summary>
        /// 从对象池获取游戏对象（使用原始Transform）
        /// </summary>
        public static GameObject Spawn(this GameObject prefab, Transform parent = null)
        {
            return ObjectPoolsManager.Instance.Spawn(prefab, Vector3.zero, Quaternion.identity, parent);
        }

        /// <summary>
        /// 异步延迟后执行动作（UniTask 快捷封装，替代手写 StartCoroutine）
        /// </summary>
        public static async UniTask DelayAction(this GameObject go, float seconds, System.Action action)
        {
            if (seconds > 0) await UniTask.Delay(Mathf.Max(0, (int)(seconds * 1000)));
            action?.Invoke();
        }
    }
}
