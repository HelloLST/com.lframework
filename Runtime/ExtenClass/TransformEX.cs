using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LFramework
{
    public static class TransformEX
    {
        /// <summary>
        /// Transform 重置
        /// </summary>
        /// <param name="trans"></param>
        public static void Identity(this Transform trans)
        {
            trans.localPosition = Vector3.zero;
            trans.localScale = Vector3.one;
            trans.localRotation = Quaternion.identity;
        }

        public static void Identity(this MonoBehaviour monoBehaviour)
        {
            monoBehaviour.transform.Identity();
        }

        public static void SetLocalPosX(this Transform trans, float x)
        {
            var localPos = trans.localPosition;
            localPos.x = x;
            trans.localPosition = localPos;
        }

        public static void SetLocalPosY(this Transform trans, float y)
        {
            var localPos = trans.localPosition;
            localPos.y = y;
            trans.localPosition = localPos;
        }

        public static void SetLocalPosZ(this Transform trans, float z)
        {
            var localPos = trans.localPosition;
            localPos.z = z;
            trans.localPosition = localPos;
        }

        public static void SetLocalPosXY(this Transform transform, float x, float y)
        {
            var localPos = transform.localPosition;
            localPos.x = x;
            localPos.y = y;
            transform.localPosition = localPos;
        }

        public static void SetLocalPosYZ(this Transform transform, float y, float z)
        {
            var localPos = transform.localPosition;
            localPos.y = y;
            localPos.z = z;
            transform.localPosition = localPos;
        }

        public static void SetLocalPosXZ(this Transform transform, float x, float z)
        {
            var localPos = transform.localPosition;
            localPos.x = x;
            localPos.z = z;
            transform.localPosition = localPos;
        }

        public static void SetLocalPos(this Transform transform, float x, float y, float z)
        {
            transform.localPosition = new Vector3(x, y, z);
        }

        public static void SetLocalPositionYAdd(this Transform transform, float newY)
        {
            transform.localPosition = new Vector3(transform.localPosition.x, transform.localPosition.y + newY, transform.localPosition.z);
        }

        public static void SetLocalRotX(this Transform transform, float angle)
        {
            transform.localRotation =
                Quaternion.Euler(new Vector3(angle, transform.localRotation.eulerAngles.y,
                    transform.localRotation.eulerAngles.z));
        }

        public static void SetLocalRotY(this Transform transform, float angle)
        {
            transform.localRotation =
                Quaternion.Euler(new Vector3(transform.localRotation.eulerAngles.x, angle,
                    transform.localRotation.eulerAngles.z));
        }

        public static void SetLocalRotZ(this Transform transform, float angle)
        {
            transform.localRotation =
                Quaternion.Euler(new Vector3(transform.localRotation.eulerAngles.x, transform.localRotation.eulerAngles.y,
                    angle));
        }

        public static void SetLocalRot(this Transform transform, float x, float y, float z)
        {
            transform.localRotation = Quaternion.Euler(new Vector3(x, y, z));
        }

        public static void SetLocalScaleX(this Transform transform, float pScale)
        {
            transform.localScale = new Vector3(pScale, transform.localScale.y, transform.localScale.z);
        }

        public static void SetLocalScaleY(this Transform transform, float pScale)
        {
            transform.localScale = new Vector3(transform.localScale.x, pScale, transform.localScale.z);
        }

        public static void SetLocalScaleZ(this Transform transform, float pScale)
        {
            transform.localScale = new Vector3(transform.localScale.x, transform.localScale.y, pScale);
        }

        public static void SetLocalScale(this Transform pTran, float x, float y, float z)
        {
            pTran.localScale = new Vector3(x, y, z);
        }

        public static void SetLocalScale(this Transform pTran, Vector3 vet)
        {
            pTran.localScale = vet;
        }

        /// <summary>
        /// 获取所有的组件()，不包括本身
        /// </summary>
        /// <typeparam name="T">组件</typeparam>
        /// <param name="trans">本物体</param>
        /// <param name="includeInactive">是否激活</param>
        /// <returns></returns>
        public static T[] GetComponentsInChildExceptSelf<T>(this Transform trans, [System.ComponentModel.DefaultValue("true")] bool includeInactive = true) where T : Component
        {
            var components = trans.GetComponentsInChildren(typeof(T), includeInactive);
            var max = components.Length;
            List<T> newList = new List<T>();
            for (int idx = 0; idx < max; idx++)
            {
                if (components[idx].transform == trans)
                    continue;
                newList.Add(components[idx] as T);
            }
            return newList.ToArray();
        }

        public static void AddChild(this Transform transform, Transform childTrans)
        {
            childTrans.SetParent(transform);
        }

        /// <summary>
        /// 查找子物体 包含未激活物体
        /// </summary>
        /// <param name="self"></param>
        /// <param name="name">子物体name</param>
        /// <param name="includeInactive">是否激活</param>
        /// <returns></returns>
        public static Transform FindChildX(this Transform self, string name, bool includeInactive = true)
        {
            var childs = self.GetComponentsInChildren<Transform>(includeInactive);
            var max = childs.Length;

            for (int idx = 0; idx < max; idx++)
            {
                if (childs[idx] == null) continue;
                if (childs[idx].name == name)
                {
                    return childs[idx];
                }
            }
            return null;
        }

        /// <summary>
        /// 使用递归，查找子物体。
        /// </summary>
        /// <param name="tf"></param>
        ///<param name = "childName" > 需要查找的子物体名称 </ param >
        /// <returns></returns>
        public static Transform FindChildByName(this Transform tf, string childName)
        {
            //递归：方法内部又调用自身的过程。
            //思想：将问题转移给范围缩小的子问题。
            //步骤：查找指定名称的子物体，如果没有找到，将问题推给子物体。
            Transform childTF = tf.Find(childName);
            //如果找到 则退出
            if (childTF != null) return childTF;
            //如果没有找到，则将问题推给子物体。
            for (int i = 0; i < tf.childCount; i++)
            {
                // tf.GetChild(i) 子物体
                childTF = tf.GetChild(i).FindChildByName(childName);
                if (childTF != null) return childTF;
            }
            return null;
        }

        /// <summary>
        /// 获取组件 若没有就添加该组件
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="origin"></param>
        /// <returns></returns>
        public static T GetOrAddComponent<T>(this Transform origin) where T : Component
        {
            T component = origin.GetComponent<T>();
            if (component == null)
            {
                component = origin.gameObject.AddComponent<T>();
            }
            return component;
        }

        /// <summary>
        /// 遍历删除所有子物体
        /// </summary>
        /// <param name="trans"></param>
        public static void DestroyChildren(this Transform trans)
        {
            foreach (Transform child in trans)
            {
                if (child == trans) continue;
                GameObject.Destroy(child.gameObject);
            }
        }

        /// <summary>
        /// 设置CanvasGroup属性
        /// </summary>
        /// <param name="self"></param>
        /// <param name="active"></param>
        public static void SetCanvasGroup(this Transform self, bool active)
        {
            if (!(self is RectTransform))
                return;

            if (!self.TryGetComponent<CanvasGroup>(out var canvasGroup))
                canvasGroup = self.gameObject.AddComponent<CanvasGroup>();

            canvasGroup.alpha = active ? 1 : 0;
            canvasGroup.blocksRaycasts = active;
            canvasGroup.interactable = active;
        }

        /// <summary>
        /// 缓动注视目标方向旋转
        /// </summary>
        /// <param name="tf"></param>
        /// <param name="targetDir">目标方向</param>
        /// <param name="rotateSpeed">旋转速度</param>
        public static void LookDirection(this Transform tf, Vector3 targetDir, float rotateSpeed)
        {
            if (targetDir == Vector3.zero) return;
            //注视旋转到目标方向
            Quaternion dir = Quaternion.LookRotation(targetDir);
            //插值旋转（由快到慢）
            tf.rotation = Quaternion.Lerp(tf.rotation, dir, Time.deltaTime * rotateSpeed);
        }

        /// <summary>
        /// 缓动注视目标点旋转
        /// </summary>
        /// <param name="tf"></param>
        /// <param name="pos">目标位置</param>
        /// <param name="rotateSpeed">旋转速度</param>
        public static void LookPosition(this Transform tf, Vector3 pos, float rotateSpeed)
        {
            Vector3 dir = pos - tf.position;
            tf.LookDirection(dir, rotateSpeed);
        }
    }
}