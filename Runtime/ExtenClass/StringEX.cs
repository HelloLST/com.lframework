using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace LFramework
{
    public static class StringEx
    {
        private static readonly Dictionary<string, Regex> _regexCache = new Dictionary<string, Regex>();

        public static int Toint(this string s, int defaultValue = 0)
        {
            return int.TryParse(s, out int i) ? i : defaultValue;
        }

        public static bool Tobool(this string s, bool defaultValue = false)
        {
            return bool.TryParse(s, out bool b) ? b : defaultValue;
        }

        public static float Tosingle(this string s, float defaultValue = 0f)
        {
            return float.TryParse(s, out float f) ? f : defaultValue;
        }

        public static string Tostring(this string s, string defaultValue = "")
        {
            return string.IsNullOrEmpty(s) ? defaultValue : s;
        }

        // 移除前缀字符串
        public static string RemovePrefixString(this string val, string str)
        {
            if (string.IsNullOrEmpty(str)) return val;
            return val.StartsWith(str) ? val.Substring(str.Length) : val;
        }

        // 移除后缀字符串
        public static string RemoveSuffixString(this string self, string str)
        {
            if (string.IsNullOrEmpty(str)) return self;
            return self.EndsWith(str) ? self.Substring(0, self.Length - str.Length) : self;
        }

        //是否为EMail
        public static bool IsEmail(this string self)
        {
            return self.RegexMatch(@"^\w+([-+.]\w+)*@\w+([-.]\w+)*\.\w+([-.]\w+)*$");
        }

        public static bool IsMobilePhone(this string self)
        {
            return self.RegexMatch(@"^(13[0-9]|14[5|7]|15[0|1|2|3|5|6|7|8|9]|18[0|1|2|3|5|6|7|8|9])\d{8}$");
        }

        public static bool RegexMatch(this string slef, string pattern)
        {
            if (string.IsNullOrEmpty(pattern)) return false;
            if (!_regexCache.TryGetValue(pattern, out Regex reg))
            {
                reg = new Regex(pattern, RegexOptions.Compiled);
                _regexCache[pattern] = reg;
            }
            return reg.Match(slef).Success;
        }

        // 是否为IP地址
        public static bool IsIP(this string self)
        {
            return self.RegexMatch(@"((?:(?:25[0-5]|2[0-4]\d|[01]?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|[01]?\d?\d))");
        }

        // 判断字符串是否Int
        public static bool IsInt(this string self)
        {
            if (string.IsNullOrEmpty(self))
            {
                return false;
            }

            var ret = Regex.IsMatch(self, @"^\d+$");
            return ret;
        }

        // 判断字符串是否float
        public static bool IsDoubleOrFloat(string strTemp)
        {
            if (string.IsNullOrEmpty(strTemp))
            {
                return false;
            }
            var ret = double.TryParse(strTemp, System.Globalization.NumberStyles.Float,
                System.Globalization.NumberFormatInfo.InvariantInfo, out double num);
            return ret;
        }
    }
}