using System.Collections.Generic;
using UnityEngine;

namespace LFramework
{
    /// <summary>
    /// 有限状态机（泛型）。
    /// 状态 ID 与转换条件类型由使用方指定，框架不感知任何具体状态。
    ///
    /// 用法见 Game/Example 下的 Idle_FsmState / Run_FsmState 与 Sample/FSMDemo。
    /// </summary>
    public class FSMSystem<TStateId, TTransition>
    {
        // 当前状态机下有哪些状态
        private readonly Dictionary<TStateId, FSMState<TStateId, TTransition>> states
            = new Dictionary<TStateId, FSMState<TStateId, TTransition>>();

        // 状态机当前处于什么状态
        private FSMState<TStateId, TTransition> currentState;

        public FSMState<TStateId, TTransition> CurrentState => currentState;

        /// <summary>添加状态</summary>
        public void AddState(FSMState<TStateId, TTransition> state)
        {
            if (state == null)
            {
                Debug.LogError("The state you went to add is null");
                return;
            }
            if (states.ContainsKey(state.ID))
            {
                Debug.LogError("The state " + state.ID + " you want to add has already been added.");
                return;
            }
            state.fsm = this;
            states.Add(state.ID, state);
        }

        /// <summary>从状态机移除状态</summary>
        public void DeleteState(FSMState<TStateId, TTransition> state)
        {
            if (state == null)
            {
                Debug.LogError("The state you went to Delete is null");
                return;
            }
            if (!states.ContainsKey(state.ID))
            {
                Debug.LogError("The state " + state.ID + " you want to Delete is not exit.");
                return;
            }
            states.Remove(state.ID);
        }

        /// <summary>控制状态之间的转换</summary>
        public void PerformTransition(TTransition trans)
        {
            if (IsDefault(trans))
            {
                Debug.LogError("NullTransition is not allowed for a real transition.");
                return;
            }
            if (currentState == null)
            {
                Debug.LogError("FSM PerformTransition failed: 状态机尚未启动，请先调用 Start()");
                return;
            }

            TStateId id = currentState.GetOutState(trans);// 判断发生这个条件的时候是否可发生转换
            if (IsDefault(id))
            {
                Debug.Log("Transition is not tobe happend!没有符合条件的转换");
                return;
            }

            if (!states.TryGetValue(id, out FSMState<TStateId, TTransition> state) || state == null)
            {
                Debug.LogError($"FSM PerformTransition failed: 目标状态 {id} 未注册");
                return;
            }

            currentState.DoBeforeLeaving();
            currentState = state;
            currentState.DoBeforeEntering();
        }

        /// <summary>设置默认状态并启动状态机</summary>
        public void Start(TStateId id)
        {
            if (states.TryGetValue(id, out FSMState<TStateId, TTransition> state))
            {
                currentState = state;
                state.DoBeforeEntering();
            }
            else
            {
                Debug.LogError("The state" + id + "is not exit in the fsm.");
            }
        }

        private static bool IsDefault<TValue>(TValue value)
            => EqualityComparer<TValue>.Default.Equals(value, default);
    }
}
