using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace LFramework.Util
{
    public static class UnityHelper
    {
        /// <summary>
        /// 根据预制体对象创建实例子物体
        /// </summary>
        /// <param name="trans"></param>
        /// <param name="prefab"></param>
        /// <param name="name"></param>
        /// <returns></returns>
        public static Transform AddChildFromPrefab(Transform trans, Transform prefab, string name = null)
        {
            if (prefab == null)
            {
                Debug.LogException(new Exception("prefab is null"));
                return null;
            }
            Transform childTrans = UnityEngine.Object.Instantiate(prefab) as Transform;
            childTrans.SetParent(trans, false);
            childTrans.Identity();
            if (!string.IsNullOrEmpty(name))
            {
                childTrans.gameObject.name = name;
            }
            return childTrans;
        }

        /// <summary>
        /// 获取物体所有的Child列表,排除自己
        /// </summary>
        /// <param name="parentObj">父对象</param>
        /// <param name="includeInactive">是否包含未激活子物体</param>
        /// <returns></returns>
        public static List<Transform> GetAllChildExcludeSelf(GameObject parentObj, bool includeInactive = true)
        {
            var childList = new List<Transform>();
            if (parentObj == null)
            {
                Debug.LogWarning("parentObj为null");
                return childList;
            }
            var childs = parentObj.GetComponentsInChildren<Transform>(includeInactive);
            for (int idx = 0; idx < childs.Length; idx++)
            {
                if (childs[idx] == parentObj.transform)
                    continue;
                childList.Add(childs[idx]);
            }
            return childList;
        }

        /// <summary>
        /// 获取程序根目录
        /// </summary>
        /// <returns></returns>
        public static string GetRootDirectory()
        {
            string dataPath = Environment.CurrentDirectory;
            string rootPath = Directory.GetParent(dataPath).ToString();
            return rootPath;
        }

        /// <summary>
        /// 秒数转时分秒显示
        /// </summary>
        /// <param name="second"></param>
        /// <returns></returns>
        public static string GetSecondString(int second)//秒数转换时分秒显示格式
        {
            return string.Format("{0:D2}", second / 3600) + ":" + string.Format("{0:D2}", second % 3600 / 60) + ":" + string.Format("{0:D2}", second % 60);
        }

        #region 不常用部分

        /// <summary>
        /// 转换类型
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="pObj"></param>
        /// <returns></returns>
        public static T ChangeType<T>(object pObj)
        {
            try
            {
                return (T)System.Convert.ChangeType(pObj, typeof(T));
            }
            catch (Exception ex)
            {
                Debug.LogError("转换类型错误：" + ex);
                return default(T);
            }
        }

        /// <summary>
        /// 切割字符串，获取列表，比如："1,2,3"转换成List<int>{1,2,3}
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="pStr"></param>
        /// <param name="pChars"></param>
        /// <returns></returns>
        public static List<T> GetList<T>(string pStr, params char[] pChars)
        {
            List<T> _list = new List<T>();
            if (pChars != null && !string.IsNullOrEmpty(pStr) && pChars.Length > 0)
            {
                string[] _arrays = pStr.Split(pChars, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < _arrays.Length; i++)
                {
                    T _temp = ChangeType<T>(_arrays[i]);
                    if (_temp != null)
                    {
                        _list.Add(_temp);
                    }
                }
            }
            if (_list.Count == 0)
            {
                Debug.LogError("列表为空！");
            }
            return _list;
        }

        #endregion 不常用部分
    }
}