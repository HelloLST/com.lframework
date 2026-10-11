using System;
using UnityEngine;

namespace LFramework.Editor
{
    /// <summary>
    /// 架构护栏规则配置（可选资产，放 Resources 下、命名 "ArchitectureGuardrailSettings"）。
    ///
    /// 分工：
    /// - **硬反模式规则**（Find / FindObjectOfType / DontDestroyOnLoad 等）是通用工程卫生问题，
    ///   由框架内置、始终生效，不需要配置。
    /// - **软提醒规则**随工程而异（例如"本工程不建议再新增 xxx 常量"），因此由本资产配置，
    ///   **框架不硬编码任何业务名词**。
    ///
    /// 未创建该资产时：硬规则照常生效，软提醒为空。
    /// </summary>
    [CreateAssetMenu(menuName = "LFramework/Architecture Guardrail Settings", fileName = "ArchitectureGuardrailSettings")]
    public class ArchitectureGuardrailSettings : ScriptableObject
    {
        [Tooltip("软提醒规则（正则）。命中时输出 Warning。例如：EventConstKey\\. 用于提示改用 SO 事件通道")]
        public string[] warningPatterns = Array.Empty<string>();

        [Tooltip("软提醒统一提示语")]
        public string warningHint = "建议改用更解耦的写法（SO 事件通道 / RuntimeSet / 依赖注入）";

        [Tooltip("额外排除的路径片段（包含即跳过扫描），如 \"/ConfigTools/\"")]
        public string[] extraExcludeDirs = Array.Empty<string>();

        private static ArchitectureGuardrailSettings _instance;
        private static bool _loaded;

        /// <summary>缓存的配置资产（可能为 null）。</summary>
        public static ArchitectureGuardrailSettings Instance
        {
            get
            {
                if (!_loaded)
                {
                    _loaded = true;
                    _instance = Resources.Load<ArchitectureGuardrailSettings>("ArchitectureGuardrailSettings");
                }
                return _instance;
            }
        }
    }
}
