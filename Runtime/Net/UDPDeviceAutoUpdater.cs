using UnityEngine;
using System.Collections.Generic;

namespace LFramework
{
    /// <summary>
    /// UDPDevice 自动更新助手
    /// 用法：将此脚本添加到场景中的 GameObject 上
    /// 它会自动调用所有活动 UDPDevice 的 Update 方法，确保事件在主线程触发
    /// </summary>
    public class UDPDeviceAutoUpdater : UnitySingleton<UDPDeviceAutoUpdater>
    {
        /// <summary>
        /// 注册需要自动更新的 UDPDevice
        /// </summary>
        public void RegisterDevice(UDPDevice device)
        {
            if (device == null)
            {
                Debug.LogWarning("UDPDeviceAutoUpdater: Attempted to register null device.");
                return;
            }

            if (!_devices.Contains(device))
            {
                _devices.Add(device);
            }
        }

        /// <summary>
        /// 注销 UDPDevice
        /// </summary>
        public void UnregisterDevice(UDPDevice device)
        {
            _devices.Remove(device);
        }

        private void Update()
        {
            if (_devices.Count == 0)
                return;

            // 创建临时副本以在迭代时安全处理可能的注销
            var devicesCopy = new System.Collections.Generic.List<UDPDevice>(_devices.Count);
            foreach (var device in _devices)
            {
                devicesCopy.Add(device);
            }

            foreach (var device in devicesCopy)
            {
                if (device != null)
                {
                    try
                    {
                        device.Update();
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"UDPDeviceAutoUpdater: Error updating device. {e.Message}");
                    }
                }
                else
                {
                    _devices.Remove(device);
                }
            }

            // 清理已销毁的设备的弱引用
            _devices.RemoveAll(d => d == null);
        }

        protected override void OnDestroy()
        {
            _devices.Clear();
        }

        private System.Collections.Generic.List<UDPDevice> _devices = new System.Collections.Generic.List<UDPDevice>();
    }
}
