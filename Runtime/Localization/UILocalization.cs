using System;
using UnityEngine;

namespace LFramework
{
    public abstract class UILocalization<T> : MonoBehaviour where T : Component
    {
        [SerializeField]
        protected T m_Target;

        [SerializeField]
        protected string m_Key;

        // 运行时替换参数（不序列化，Unity 不支持 object[] 序列化）
        protected object[] m_Args = null;

        // 设置键
        public virtual void SetKey(string key, params object[] args)
        {
            bool dirty = key != m_Key || !ArgsEqual(m_Args, args);
            m_Key = key;
            m_Args = args;
            if (dirty)
            {
                OnChange();
            }
        }

        public void OnChange()
        {
            if (!string.IsNullOrEmpty(m_Key) && m_Target != null)
            {
                RefreshShow();
            }
            // 注意：不要在此清空 m_Args，否则下次语言切换时格式化参数会丢失
        }

        // 刷新显示
        protected abstract void RefreshShow();

        protected virtual void OnEnable()
        {
            // 语言切换时通过 EventCenter 广播，此处订阅
            EventCenter.AddEventListener(LocalizationEventKeys.ChangeLanguage, OnChange);
        }

        protected virtual void OnDisable()
        {
            EventCenter.RemoveListener(LocalizationEventKeys.ChangeLanguage, OnChange);
        }

        protected virtual void Start()
        {
            // 仅通过 EventCenter 订阅语言切换；LocalizationManager.Register 会导致同一组件刷新两次
            OnChange();
        }

        // 获取本地化数据
        protected string GetLocalizationData()
        {
            string data = LocalizationManager.Instance?.GetLocalizationData(m_Key) ?? m_Key;
            if (m_Args != null && m_Args.Length > 0)
            {
                try
                {
                    return string.Format(data, m_Args);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Localization] 格式化失败 key={m_Key}: {e.Message}");
                    return data;
                }
            }
            return data;
        }

        // 获取组件
        protected virtual void OnValidate()
        {
            if (m_Target == null)
                m_Target = GetComponent<T>();
        }

        protected virtual void OnDestroy()
        {
            EventCenter.RemoveListener(LocalizationEventKeys.ChangeLanguage, OnChange);
        }

        // 比较参数数组是否一致
        private static bool ArgsEqual(object[] a, object[] b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (!Equals(a[i], b[i])) return false;
            }
            return true;
        }
    }
}
