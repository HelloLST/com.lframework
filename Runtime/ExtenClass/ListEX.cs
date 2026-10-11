using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LFramework
{
    public static class ListEx
    {
        /// <summary>
        /// 打乱 泛型列表项目
        /// </summary>
        /// <typeparam name="t"></typeparam>
        /// <param name="TList"></param>
        /// <returns></returns>
        public static List<t> DisorderItems<t>(this List<t> TList)  //
        {
            if (TList == null) return null;
            List<t> NewList = new List<t>(TList);
            NewList.SortRandom();
            return NewList;
        }

        /// <summary>
        /// 获取列表随机N个元素
        /// </summary>
        /// <param name="list"></param>
        /// <param name="count"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static List<T> GetRandomChilds<T>(this List<T> list, int count)
        {
            if (list == null || count <= 0) return new List<T>();
            List<T> tempList = new List<T>();
            // tempList = list.GetRange(0,list.Count);
            tempList.AddRange(list);
            tempList.SortRandom();
            return tempList.GetRange(0, Mathf.Min(count, tempList.Count));
        }

        /// <summary>
        /// 列表的2个元素位置调换
        /// </summary>
        public static void Swap<T>(this List<T> list, int index1, int index2)
        {
            T temp = list[index2];
            list[index2] = list[index1];
            list[index1] = temp;
        }

        /// <summary>
        /// 乱序排序列表
        /// </summary>
        public static List<T> SortRandom<T>(this List<T> list)
        {
            int randomIndex;
            for (int i = list.Count - 1; i > 0; i--)
            {
                randomIndex = UnityEngine.Random.Range(0, i);
                list.Swap(randomIndex, i);
            }
            return list;
        }

        /// <summary>
        /// 从列表给定的索引中选取连续的一组数据
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="mainlist"></param>
        /// <param name="startIndex"></param>
        /// <param name="step"></param>
        /// <returns></returns>
        public static List<List<T>> GetSomeItem<T>(this List<T> mainlist,
                                         int startIndex, int step)
        {
            var queue = new Queue<T>();
            mainlist.ForEach(queue.Enqueue);
            Enumerable.Range(0, startIndex)
                    .ToList()
                    .ForEach(i => queue.Enqueue(queue.Dequeue()));
            var result = new List<List<T>>();
            while (true)
            {
                var list = queue.Take(step).ToList();
                list.ForEach(i => queue.Enqueue(queue.Dequeue()));
                if (result.Any(l => l.All(list.Contains)))
                    break;
                result.Add(list.ToList());
            }
            return result;
        }
    }
}