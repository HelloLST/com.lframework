using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace LFramework.UI
{
    /// <summary>
    /// 控件自动绑定工具：按 btn_/img_/text_ 等前缀约定，将面板字段自动绑定到预制体节点
    /// 与编辑器工具 UIExport 的命名约定保持一致
    /// </summary>
    public static class UIComponentBinding
    {
        /// <summary>
        /// 命名约定前缀，与 UIExport.cs 的 uiTypeDic 保持一致
        /// </summary>
        public static readonly string[] Prefixes =
        {
            "btn", "img", "obj", "text", "input", "slider", "scroll", "ui"
        };

        private static readonly HashSet<string> _prefixSet = new HashSet<string>(Prefixes);

        private static readonly Dictionary<Type, FieldInfo[]> _fieldCache = new Dictionary<Type, FieldInfo[]>();

        private const BindingFlags _fieldFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>
        /// 字段名是否命中约定前缀（以下划线拆分，首段命中）
        /// </summary>
        public static bool TryGetPrefix(string fieldName, out string prefix)
        {
            prefix = null;
            if (string.IsNullOrEmpty(fieldName)) return false;
            int index = fieldName.IndexOf('_');
            if (index <= 0) return false;
            string head = fieldName.Substring(0, index);
            if (!_prefixSet.Contains(head)) return false;
            prefix = head;
            return true;
        }

        /// <summary>
        /// 反射绑定面板内所有符合约定前缀的字段
        /// </summary>
        public static void Bind(UIPanelBase panel)
        {
            if (panel == null) return;

            FieldInfo[] fields = GetFields(panel.GetType());
            Transform root = panel.transform;

            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                if (field.IsStatic || field.IsInitOnly) continue;
                if (!TryGetPrefix(field.Name, out _)) continue;

                Type fieldType = field.FieldType;
                if (fieldType != typeof(GameObject) && !typeof(Component).IsAssignableFrom(fieldType)) continue;

                Transform node = FindNode(root, field.Name);
                if (node == null)
                {
                    LogManager.Warning($"[UIComponentBinding] 面板 {panel.GetType().Name} 未找到节点: {field.Name}", panel.name);
                    continue;
                }

                object value;
                if (fieldType == typeof(GameObject))
                {
                    value = node.gameObject;
                }
                else
                {
                    value = node.GetComponent(fieldType);
                    if (value == null)
                    {
                        LogManager.Warning($"[UIComponentBinding] 节点 {field.Name} 上未找到组件: {fieldType.Name}", panel.name);
                        continue;
                    }
                }
                field.SetValue(panel, value);
            }
        }

        private static FieldInfo[] GetFields(Type type)
        {
            if (_fieldCache.TryGetValue(type, out FieldInfo[] cached)) return cached;
            FieldInfo[] fields = type.GetFields(_fieldFlags);
            _fieldCache[type] = fields;
            return fields;
        }

        /// <summary>
        /// 查找节点：先按字段名做 transform.Find（命中直接子级或路径编码名），未命中则全层级按节点名首匹配
        /// </summary>
        private static Transform FindNode(Transform root, string fieldName)
        {
            Transform direct = root.Find(fieldName);
            if (direct != null) return direct;
            return FindNodeByName(root, fieldName);
        }

        private static Transform FindNodeByName(Transform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child.name == name) return child;
                Transform nested = FindNodeByName(child, name);
                if (nested != null) return nested;
            }
            return null;
        }
    }
}
