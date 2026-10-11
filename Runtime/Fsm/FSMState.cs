using System.Collections.Generic;
using UnityEngine;

namespace LFramework
{
    /// <summary>
    /// 有限状态机状态基类（泛型）。
    ///
    /// 状态 ID 与转换条件由使用方自定义（枚举 / int / string / 任意可比较类型），
    /// 框架不预设任何具体状态 —— 换工程无需改动框架代码。
    ///
    /// 约定：TStateId / TTransition 的 default 值（枚举第 0 项，或引用类型的 null）
    /// 视为"空"，不能用于真实的状态与转换。
    /// </summary>
    public abstract class FSMState<TStateId, TTransition>
    {
        protected TStateId stateID;

        public TStateId ID => stateID;

        // 存储在不同的条件下可以转换到什么样的状态
        protected Dictionary<TTransition, TStateId> map = new Dictionary<TTransition, TStateId>();

        public FSMSystem<TStateId, TTransition> fsm;

        /// <summary>添加状态转换条件：由 trans 可切换到 id</summary>
        public void AddTransition(TTransition trans, TStateId id)
        {
            if (IsDefault(trans) || IsDefault(id))
            {
                Debug.LogError("Transition or stateid is null!!!");
                return;
            }
            if (map.ContainsKey(trans))
            {
                Debug.LogError("State" + id + " is already has transition" + trans);
                return;
            }
            map.Add(trans, id);
        }

        /// <summary>删除状态转换条件</summary>
        public void DeleteTransition(TTransition trans)
        {
            if (!map.ContainsKey(trans))
            {
                Debug.LogWarning("The transition " + trans + "you want to delete is not exit in map!!!");
                return;
            }
            map.Remove(trans);
        }

        /// <summary>根据转换条件取目标状态；无可转换时返回 default</summary>
        public TStateId GetOutState(TTransition trans)
        {
            return map.TryGetValue(trans, out TStateId id) ? id : default;
        }

        /// <summary>进入本状态之前执行（初始化）</summary>
        public virtual void DoBeforeEntering() { }

        /// <summary>离开本状态时执行（清理）</summary>
        public virtual void DoBeforeLeaving() { }

        /// <summary>状态机处于本状态时逐帧调用</summary>
        public abstract void DoUpdate();

        private static bool IsDefault<TValue>(TValue value)
            => EqualityComparer<TValue>.Default.Equals(value, default);
    }
}
