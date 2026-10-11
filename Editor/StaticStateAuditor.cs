using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace LFramework.Editor
{
    /// <summary>
    /// 静态状态审计：扫描目标程序集里的"可写静态字段"，按 Domain Reload 残留风险分级输出。
    /// 背景：本工程已关闭 Domain Reload（ProjectSettings/EditorSettings.asset 的
    /// EnterPlayModeOptions=3），未重置的静态字段会跨 Play 会话残留——
    /// 缓存假 null 的 Unity 资源、计数残留、协程句柄失效等（Phase 1 / P0-2）。
    ///
    /// 分级规则：
    ///   [Unity引用-未重置]  字段类型为 UnityEngine.Object 派生，且声明类型没有任何
    ///                       [RuntimeInitializeOnLoadMethod] 重置入口 → 高危，报 Warning
    ///   其余可写静态字段    仅计数（基元/字符串/集合缓存等，按需自查）
    ///
    /// 菜单：Tools/LFramework/架构校验/静态状态审计
    /// </summary>
    public static class StaticStateAuditor
    {
        private static readonly string[] ScannedAssemblies = { "LFramework", "Game", "Sample" };

        [MenuItem("Tools/LFramework/架构校验/静态状态审计")]
        public static void Scan()
        {
            int totalFields = 0;
            int unityRefsUnreset = 0;
            var report = new StringBuilder();

            foreach (var asmName in ScannedAssemblies)
            {
                var asm = FindAssembly(asmName);
                if (asm == null) continue;

                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = SafeTypes(e.Types); }
                catch { continue; }

                foreach (var type in types)
                {
                    if (type == null) continue;

                    const BindingFlags flags =
                        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

                    FieldInfo[] fields;
                    try { fields = type.GetFields(flags); }
                    catch { continue; }

                    bool typeHasReset = TypeHasRuntimeReset(type);
                    foreach (var f in fields)
                    {
                        if (f.IsLiteral || f.IsInitOnly) continue;  // const / readonly 免检
                        if (f.Name.Contains("<>")) continue;        // 编译器生成（闭包/状态机）免检
                        totalFields++;

                        bool isUnityRef = typeof(UnityEngine.Object).IsAssignableFrom(f.FieldType);
                        if (isUnityRef && !typeHasReset)
                        {
                            unityRefsUnreset++;
                            report.AppendLine($"  {asmName} :: {type.FullName}.{PrettyName(f)} ({f.FieldType.Name})");
                        }
                    }
                }
            }

            if (unityRefsUnreset == 0)
            {
                Debug.Log($"[静态审计] ✅ 未发现\"持有 Unity 对象且无重置入口\"的静态字段。共扫描可写静态字段 {totalFields} 个。");
            }
            else
            {
                Debug.LogWarning(
                    "[静态审计] ⚠ 发现 " + unityRefsUnreset + " 个高危静态字段" +
                    "（关闭 Domain Reload 时跨 Play 残留假 null 风险，建议补 [RuntimeInitializeOnLoadMethod] 重置）：\n" + report);
                Debug.Log($"[静态审计] 共扫描可写静态字段 {totalFields} 个；其余为普通静态（基元/集合缓存），不在高危分级内。");
            }
        }

        /// <summary>自动属性的后备字段形如 "&lt;Canvas&gt;k__BackingField"，还原成属性名便于阅读。</summary>
        private static string PrettyName(FieldInfo f)
        {
            string name = f.Name;
            int lt = name.IndexOf('<');
            int gt = name.IndexOf('>', lt + 1);
            if (lt >= 0 && gt > lt)
                return name.Substring(lt + 1, gt - lt - 1) + "（自动属性）";
            return name;
        }

        private static Assembly FindAssembly(string name)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetName().Name == name) return asm;
            }
            return null;
        }

        private static Type[] SafeTypes(Type[] types)
        {
            var list = new List<Type>();
            foreach (var t in types)
            {
                if (t != null) list.Add(t);
            }
            return list.ToArray();
        }

        private static bool TypeHasRuntimeReset(Type type)
        {
            MethodInfo[] methods;
            try
            {
                methods = type.GetMethods(
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            }
            catch { return false; }

            foreach (var m in methods)
            {
                object[] attrs;
                try { attrs = m.GetCustomAttributes(false); }
                catch { continue; }

                foreach (var a in attrs)
                {
                    if (a is RuntimeInitializeOnLoadMethodAttribute) return true;
                }
            }
            return false;
        }
    }
}
