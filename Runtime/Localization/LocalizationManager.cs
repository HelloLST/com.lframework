using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace LFramework
{
    public class LocalizationManager : SingletonBase<LocalizationManager>, IModule
    {
        private LocalizationManager()
        { }

        // 默认配置文件名（StreamingAssets 下的 JSON，运行时不再依赖 EPPlus 解析 xlsx）
        private const string k_DefaultConfigName = "Localization.json";

        // 当前的语言
        private Language m_Language = Language.Chinese;

        public Language Language => m_Language;

        public string ConfigPath { get => m_ConfigPath; set => m_ConfigPath = value; }

        // 配置文件路径
        private string m_ConfigPath;

        // 每种语言一份 key→value 字典；切换语言不重读表
        private Dictionary<Language, Dictionary<string, string>> m_LanguageTable;

        // 当前语言对应的字典
        private Dictionary<string, string> m_CurrentTable;

        // 注册的回调
        private Action m_Callback;

        private bool isLoadCompleted;

        protected override void Init()
        {
            ConfigPath = k_DefaultConfigName;
            m_LanguageTable = new Dictionary<Language, Dictionary<string, string>>();
            // 不在此自动加载与切语言；由业务（GameLaunch）显式调用 InitializeAsync。
        }

        /// <summary>
        /// 一次性完成"加载配置表 + 设置初始语言"。推荐在 GameLaunch.Start 中使用。
        /// </summary>
        /// <param name="initialLanguage">初始语言（可来自 Inspector / 外部配置）</param>
        /// <param name="configName">可选：覆盖默认表名（StreamingAssets 下的 JSON 文件）</param>
        public async UniTask InitializeAsync(Language initialLanguage, string configName = null)
        {
            if (!string.IsNullOrEmpty(configName))
            {
                ConfigPath = configName;
            }
            await LoadConfigAsync(ConfigPath);
            SetLanguage(initialLanguage);
        }

        /// <summary>
        /// 重新加载配置表（外部表更新后调用）；保持当前语言并触发 UI 刷新。
        /// </summary>
        public async UniTask ReloadAsync(string configName = null)
        {
            if (!string.IsNullOrEmpty(configName))
            {
                ConfigPath = configName;
            }
            isLoadCompleted = false;
            await LoadConfigAsync(ConfigPath);
            SetLanguage(m_Language); // 用当前语言刷新一次，触发 UI 重读
        }

        // 设置语言
        public void SetLanguage(Language language)
        {
            if (!isLoadCompleted)
            {
                Debug.LogWarning($"[Localization] 配置表尚未加载，切换到 {language} 后字典为空。请先调用 LoadConfigAsync / InitializeAsync。");
            }

            m_Language = language;

            if (m_LanguageTable.TryGetValue(m_Language, out var table))
            {
                m_CurrentTable = table;
            }
            else
            {
                Debug.LogWarning($"[Localization] 语言 {m_Language} 未在配置表中找到，回退为空字典");
                m_CurrentTable = new Dictionary<string, string>();
            }

            // 无条件广播：首次设置语言时也应让已订阅 ChangeLanguage 的 UI 刷新一次
            m_Callback?.Invoke();
            EventCenter.TriggerEvent(LocalizationEventKeys.ChangeLanguage);
        }

        // 同步加载配置（旧接口保留，内部同样读取 JSON）
        public void LoadConfig(string configName)
        {
            string filePath = Path.Combine(Application.streamingAssetsPath, configName);
            if (!File.Exists(filePath))
            {
                Debug.LogError($"[Localization] 配置文件不存在: {filePath}");
                return;
            }

            try
            {
                var parsed = ParseConfig(filePath);
                if (parsed == null)
                {
                    Debug.LogError($"[Localization] 配置文件解析失败（缺少语言列）: {filePath}");
                    return;
                }
                m_LanguageTable = parsed;
                isLoadCompleted = true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Localization] 读取 JSON 失败: {e}");
            }
        }

        // 异步加载配置，避免启动时同步 IO 阻塞主线程
        public async UniTask LoadConfigAsync(string configName)
        {
            string filePath = Path.Combine(Application.streamingAssetsPath, configName);
            if (!File.Exists(filePath))
            {
                Debug.LogError($"[Localization] 配置文件不存在: {filePath}（请先通过菜单 LFramework/Localization/导出 Excel 到 JSON 生成）");
                return;
            }

            try
            {
                var parsed = await UniTask.RunOnThreadPool(() => ParseConfig(filePath));
                if (parsed == null)
                {
                    Debug.LogError($"[Localization] 配置文件解析失败（缺少语言列）: {filePath}");
                    return;
                }

                m_LanguageTable = parsed;
                isLoadCompleted = true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Localization] 异步读取 JSON 失败: {e}");
            }
        }

        /// <summary>
        /// 解析 JSON：{"Chinese": {"key": "值", ...}, "English": {...}}
        /// 语言键必须是 Language 枚举名（Chinese / English ...）
        /// </summary>
        private static Dictionary<Language, Dictionary<string, string>> ParseConfig(string filePath)
        {
            string json = File.ReadAllText(filePath);
            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Localization] JSON 解析失败: {e.Message}");
                return null;
            }

            if (root == null || root.Count == 0)
            {
                return null;
            }

            var result = new Dictionary<Language, Dictionary<string, string>>();
            foreach (var langProp in root.Properties())
            {
                if (!Enum.TryParse(langProp.Name, true, out Language lang))
                {
                    continue;
                }

                var dict = new Dictionary<string, string>();
                if (langProp.Value is JObject langObj)
                {
                    foreach (var kv in langObj.Properties())
                    {
                        dict[kv.Name] = kv.Value?.ToString() ?? string.Empty;
                    }
                }
                result[lang] = dict;
            }

            if (result.Count == 0)
            {
                Debug.LogError("[Localization] 配置文件中未识别到任何语言键，请使用语言枚举名（Chinese / English ...）");
                return null;
            }
            return result;
        }

        // 获取本地化数据
        public string GetLocalizationData(string key)
        {
            if (m_CurrentTable == null)
            {
                Debug.LogWarning($"[Localization] 当前语言字典未初始化，返回 key: {key}");
                return key;
            }

            if (!m_CurrentTable.TryGetValue(key, out string data))
            {
                Debug.LogWarning($"[Localization] key 不存在: {key}");
                return key;
            }
            return data;
        }

        // 是否包含指定 key
        public bool HasKey(string key)
        {
            return m_CurrentTable != null && m_CurrentTable.ContainsKey(key);
        }

        // 注册更改语言后的回调
        public void Register(Action callback)
        {
            m_Callback += callback;
        }

        // 取消注册回调
        public void Unregister(Action callback)
        {
            m_Callback -= callback;
        }

        #region IModule 实现

        public string DisplayName => GetType().Name;
        public UniTask InitializeAsync() => UniTask.CompletedTask;
        public void Tick(float deltaTime) { }
        public void Dispose() { }

        #endregion
    }
}
