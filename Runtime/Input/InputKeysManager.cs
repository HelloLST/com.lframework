using System;
using System.Collections.Generic;
using UnityEngine;

namespace LFramework
{
    /// <summary>
    /// 按键输入管理器（按需轮询版）
    /// 只轮询通过 RegisterKey 注册的按键，避免旧实现每帧遍历全部 KeyCode（约 450 个）的巨额开销。
    /// 未注册任何按键时每帧只做 9 次鼠标查询，几乎零开销。
    /// </summary>
    public class InputKeysManager : SingletonBase<InputKeysManager>
    {
        //需要轮询的按键集合
        private readonly HashSet<KeyCode> _registeredKeys = new HashSet<KeyCode>();

        //是否启用本脚本的功能，监听玩家按键输入。
        public bool IsActive { get; private set; }

        /// <summary>
        /// 是否激活按键监听
        /// 传入true，会激活按键监听，这样玩家通过按键输入管理器输入的操作才会生效。
        /// 传入false，会禁用按键监听，这样玩家通过按键输入管理器输入的操作就会无效。
        /// </summary>
        public void SetActive(bool isActive)
        {
            IsActive = isActive;
        }

        public InputKeysManager()
        {
            MonoManager.Instance.AddUpdateListener(CheckKeys);
        }

        /// <summary>注册需要轮询的按键（重复注册无副作用）</summary>
        public void RegisterKey(KeyCode key)
        {
            _registeredKeys.Add(key);
        }

        /// <summary>注销按键</summary>
        public void UnregisterKey(KeyCode key)
        {
            _registeredKeys.Remove(key);
        }

        /// <summary>清空所有已注册按键</summary>
        public void ClearRegisteredKeys()
        {
            _registeredKeys.Clear();
        }

        /// <summary>当前已注册按键数量</summary>
        public int RegisteredKeyCount => _registeredKeys.Count;

        private void CheckKeys()
        {
            if (!IsActive) return;

            // 只轮询业务注册过的按键
            foreach (var key in _registeredKeys)
            {
                if (Input.GetKeyDown(key))
                {
                    EventCenter.TriggerEvent(InputEventKeys.KeyDown, key);
                }
                if (Input.GetKeyUp(key))
                {
                    EventCenter.TriggerEvent(InputEventKeys.KeyUp, key);
                }
                if (Input.GetKey(key))
                {
                    EventCenter.TriggerEvent(InputEventKeys.Key, key);
                }
            }

            // 鼠标按键固定轮询 0/1/2
            for (int i = 0; i <= 2; i++)
            {
                if (Input.GetMouseButtonDown(i))
                {
                    EventCenter.TriggerEvent(InputEventKeys.MouseButtonDown, i);
                }
                if (Input.GetMouseButtonUp(i))
                {
                    EventCenter.TriggerEvent(InputEventKeys.MouseButtonUp, i);
                }
                if (Input.GetMouseButton(i))
                {
                    EventCenter.TriggerEvent(InputEventKeys.MouseButton, i);
                }
            }
        }
    }
}
