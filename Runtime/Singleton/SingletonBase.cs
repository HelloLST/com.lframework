using System;
using UnityEngine;

namespace LFramework
{
    /// <summary>
    /// 不继承MonoBehaviour的单例模式基类
    /// </summary>
    public class SingletonBase<T> where T : SingletonBase<T>//泛型T必须为这个类本身或者是它的子类
    {
        //构造方法私有化，防止外部new对象。
        protected SingletonBase()
        { }

        //线程锁。当多线程访问时，同一时刻仅允许一个线程访问。
        private static object locker = new object();

        //提供一个属性给外部访问，这个属性就相当于是单例对象。
        //volatile关键字修饰的字段，当多个线程都对它进行修改时，可以确保这个字段在任何时刻呈现的都是最新的值。
        private static volatile T instance;

        /// <summary>
        /// 关闭 Domain Reload 时静态实例会跨 Play 会话残留：
        /// 第二次进 Play 拿到的还是上一个会话的实例，其缓存里的 Unity 资源全部是假 null，
        /// 导致 Load 命中缓存却返回失效对象。进入 Play 时强制清空，实例由下一次访问惰性重建
        /// （Phase 1 / P0-2 修复）。Domain Reload 开启时静态本就会被清空，此方法幂等无害。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetInstanceForNewSession()
        {
            instance = null;
        }

        public static T Instance
        {
            get
            {
                if (instance == null)
                {
                    lock (locker)
                    {
                        //保证对象的唯一性。
                        if (instance == null)
                        {
                            instance = Activator.CreateInstance(typeof(T), true) as T;//使用反射，调用无参构造方法创建对象。
                            instance.Init();
                        }
                    }
                }
                return instance;
            }
        }

        /// <summary>
        /// 可选初始化函数
        /// </summary>
        protected virtual void Init()
        {
        }
    }
}
