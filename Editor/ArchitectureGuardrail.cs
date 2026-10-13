using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace LFramework.Editor
{
    /// <summary>
    /// 架构护栏：在保存/编译/构建时扫描【业务层】脚本，拦截会破坏解耦架构的反模式。
    ///
    /// 作用域：仅扫描 Assets/Scripts 下、排除 Core/ThirdParty/Editor/Tests/Plugins 的业务代码。
    /// 框架内部（Core）允许使用底层 API（单例、查找等），不在校验范围内。
    ///
    /// 行内豁免：在需要跳过的代码行末尾加  // ARCH_EXEMPT  即可忽略该行（用于已知技术债）。
    ///
    /// 触发方式：
    ///   1) 保存 .cs 时自动扫描改动的文件（AssetPostprocessor）
    ///   2) 菜单 Tools/LFramework/架构校验/全量扫描业务层
    ///   3) 可选：开启「构建时拦截反模式」后，构建前若有硬反模式则中止构建
    /// </summary>
    public static class ArchitectureGuardrail
    {
        private static string ScriptRoot => Application.dataPath + "/Scripts";

        // 排除目录（框架内部、第三方、编辑器、测试、插件、StreamingAssets）
        private static readonly string[] ExcludeDirs =
        {
            "/Core/", "/ThirdParty/", "/Editor/", "/Tests/", "/TestsPlayMode/", "/Plugins/", "/StreamingAssets/"
        };

        // 硬反模式（Error）：业务层禁止的跨系统查找 / 单例滥用
        // 注意：模式都要求紧邻 "("，避免误伤注释与散文描述。
        private static readonly (string Pattern, string Hint)[] HardRules =
        {
            (@"GameObject\.Find\(", "禁止 GameObject.Find —— 用 RuntimeSet 替代"),
            (@"GameObject\.FindGameObjectWithTag\(", "禁止 FindGameObjectWithTag —— 用 RuntimeSet + TransformSetRegistrar 替代"),
            (@"GameObject\.FindGameObjectsWithTag\(", "禁止 FindGameObjectsWithTag —— 用 RuntimeSet 替代"),
            (@"FindObjectOfType\(", "禁止 FindObjectOfType —— 用 ScriptableObject / RuntimeSet 解耦"),
            (@"FindFirstObjectByType\(", "禁止 FindFirstObjectByType —— 用 ScriptableObject / RuntimeSet 解耦"),
            (@"FindAnyObjectByType\(", "禁止 FindAnyObjectByType —— 用 ScriptableObject / RuntimeSet 解耦"),
            (@"Resources\.FindObjectsOfTypeAll\(", "禁止 Resources.FindObjectsOfTypeAll —— 用 RuntimeSet 替代"),
            (@"DontDestroyOnLoad\(", "禁止业务层 DontDestroyOnLoad 单例 —— 框架 Core 内允许"),
        };

        // 软提醒（Warning）：随工程而异，由 ArchitectureGuardrailSettings 资产配置。
        // 框架不硬编码任何业务名词；未配置资产时软提醒为空（硬规则照常生效）。
        private static (string Pattern, string Hint)[] GetSoftRules()
        {
            var settings = ArchitectureGuardrailSettings.Instance;
            if (settings == null || settings.warningPatterns == null || settings.warningPatterns.Length == 0)
                return System.Array.Empty<(string, string)>();

            var list = new List<(string, string)>();
            foreach (var pattern in settings.warningPatterns)
            {
                if (!string.IsNullOrWhiteSpace(pattern))
                    list.Add((pattern, settings.warningHint));
            }
            return list.ToArray();
        }

        private const string ExemptToken = "ARCH_EXEMPT";

        /// <summary>按工程持久化，默认关闭，避免误伤现有工程。</summary>
        public static bool FailBuildOnHardViolation
        {
            get => EditorPrefs.GetBool(BuildBlockPreferenceKey, false);
            set => EditorPrefs.SetBool(BuildBlockPreferenceKey, value);
        }

        public static string BuildBlockPreferenceKey =>
            "LFramework.ArchitectureGuardrail.FailBuild:" + Application.dataPath.Replace("\\", "/");

        public static string ReportPath => Path.Combine(
            Directory.GetParent(Application.dataPath).FullName, "Artifacts/ArchitectureGuardrail/report.json");

        [System.Serializable]
        public sealed class ScanReport
        {
            public string GeneratedAtUtc;
            public bool FailBuildEnabled;
            public int ScannedFiles;
            public int Errors;
            public int Warnings;
            public int ReadFailures;
            public List<Violation> Violations = new List<Violation>();
        }

        [System.Serializable]
        public sealed class Violation
        {
            public string AssetPath;
            public int Line;
            public string Severity;
            public string Hint;
            public string Source;
        }

        // 全量扫描、构建和 CI 共用同一报告与规则。
        [MenuItem("Tools/LFramework/架构校验/全量扫描业务层")]
        public static void ScanAll()
        {
            var report = ScanProject();
            Debug.Log($"[架构护栏] 扫描完成：{report.ScannedFiles} 个文件，{report.Errors} 个错误，{report.Warnings} 个提醒，{report.ReadFailures} 个读取失败。报告：{ReportPath}");
        }

        public static ScanReport ScanProject(bool logViolations = true)
        {
            var report = new ScanReport
            {
                GeneratedAtUtc = System.DateTime.UtcNow.ToString("o"),
                FailBuildEnabled = FailBuildOnHardViolation
            };
            // 空工程没有 Scripts 目录时，合法地产出零文件报告。
            if (Directory.Exists(ScriptRoot))
            {
                var files = Directory.GetFiles(ScriptRoot, "*.cs", SearchOption.AllDirectories);
                System.Array.Sort(files, System.StringComparer.Ordinal);
                foreach (var full in files)
                {
                    var assetPath = ToAssetPath(full);
                    if (IsExcluded(assetPath)) continue;
                    report.ScannedFiles++;
                    ScanFileIntoReport(assetPath, report, logViolations);
                }
            }
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true), new System.Text.UTF8Encoding(false));
            return report;
        }

        [MenuItem("Tools/LFramework/架构校验/构建时拦截反模式", true)]
        private static bool ValidateBuildBlock()
        {
            Menu.SetChecked("Tools/LFramework/架构校验/构建时拦截反模式", FailBuildOnHardViolation);
            return true;
        }

        // ---- 入口：构建前拦截（可选） ----
        [MenuItem("Tools/LFramework/架构校验/构建时拦截反模式")]
        public static void ToggleBuildBlock()
        {
            FailBuildOnHardViolation = !FailBuildOnHardViolation;
            Menu.SetChecked("Tools/LFramework/架构校验/构建时拦截反模式", FailBuildOnHardViolation);
            Debug.Log($"[架构护栏] 构建时拦截反模式：{(FailBuildOnHardViolation ? "已开启" : "已关闭")}（开启后构建前若有硬反模式将中止构建）");
        }

        // ---- 扫描单个文件，返回 (错误数, 提醒数) ----
        internal static (int errors, int warnings) ScanFile(string assetPath)
        {
            var report = new ScanReport();
            ScanFileIntoReport(assetPath, report, true);
            return (report.Errors, report.Warnings);
        }

        private static void ScanFileIntoReport(string assetPath, ScanReport report, bool logViolations)
        {
            string[] lines;
            try { lines = File.ReadAllLines(assetPath); }
            catch (System.Exception e)
            {
                report.ReadFailures++;
                report.Violations.Add(new Violation { AssetPath = assetPath, Severity = "ReadFailure", Hint = e.Message });
                if (logViolations) Debug.LogWarning($"[架构护栏] 无法读取 {assetPath}：{e.Message}");
                return;
            }
            var softRules = GetSoftRules();
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.Contains(ExemptToken)) continue;
                foreach (var rule in HardRules)
                {
                    if (!Regex.IsMatch(line, rule.Pattern)) continue;
                    report.Errors++;
                    report.Violations.Add(new Violation { AssetPath = assetPath, Line = i + 1, Severity = "Error", Hint = rule.Hint, Source = line.Trim() });
                    if (logViolations) Debug.LogError($"[架构护栏][错误] {assetPath}:{i + 1} — {rule.Hint}\n    {line.Trim()}");
                }
                foreach (var rule in softRules)
                {
                    if (!Regex.IsMatch(line, rule.Pattern)) continue;
                    report.Warnings++;
                    report.Violations.Add(new Violation { AssetPath = assetPath, Line = i + 1, Severity = "Warning", Hint = rule.Hint, Source = line.Trim() });
                    if (logViolations) Debug.LogWarning($"[架构护栏][提醒] {assetPath}:{i + 1} — {rule.Hint}\n    {line.Trim()}");
                }
            }
        }

        internal static bool IsExcluded(string assetPath)
        {
            string normalized = assetPath.Replace("\\", "/");
            foreach (var dir in ExcludeDirs)
            {
                if (normalized.Contains(dir)) return true;
            }

            // 工程可在 ArchitectureGuardrailSettings 中追加排除项
            var settings = ArchitectureGuardrailSettings.Instance;
            if (settings != null && settings.extraExcludeDirs != null)
            {
                foreach (var dir in settings.extraExcludeDirs)
                {
                    if (!string.IsNullOrEmpty(dir) && normalized.Contains(dir)) return true;
                }
            }
            return false;
        }

        private static string ToAssetPath(string fullPath)
        {
            return fullPath.Replace("\\", "/").Replace(Application.dataPath.Replace("\\", "/"), "Assets");
        }
    }

    /// <summary>保存 .cs 时自动扫描改动文件，及时发现架构回潮。</summary>
    public class ArchitectureGuardrailPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            int errors = 0, warnings = 0;
            foreach (var assetPath in importedAssets)
            {
                if (!assetPath.EndsWith(".cs", System.StringComparison.OrdinalIgnoreCase)) continue;
                // 只校验工程自有代码：排除 Packages / Library 等外部程序集（第三方包、MCP 插件等）
                if (!assetPath.StartsWith("Assets/", System.StringComparison.Ordinal)) continue;
                if (ArchitectureGuardrail.IsExcluded(assetPath)) continue;
                var (e, w) = ArchitectureGuardrail.ScanFile(assetPath);
                errors += e;
                warnings += w;
            }

            if (errors > 0)
                Debug.LogError($"[架构护栏] 保存时检测到 {errors} 处架构反模式，请查看上方 Error 日志。");
            else if (warnings > 0)
                Debug.Log($"[架构护栏] 保存时扫描完成：{warnings} 个提醒（无错误）。");
        }
    }

    /// <summary>构建前拦截：开启后，业务层存在硬反模式则中止构建，强制在 CI / 出包前修复。</summary>
    public class ArchitectureGuardrailBuildGuard : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var scan = ArchitectureGuardrail.ScanProject(ArchitectureGuardrail.FailBuildOnHardViolation);
            if (scan.FailBuildEnabled && (scan.Errors > 0 || scan.ReadFailures > 0))
            {
                throw new BuildFailedException(
                    $"[架构护栏] 构建被拦截：{scan.Errors} 处架构反模式，{scan.ReadFailures} 个文件无法读取。报告：{ArchitectureGuardrail.ReportPath}");
            }
        }
    }
}
