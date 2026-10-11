namespace LFramework
{
    /// <summary>
    /// 框架级本地化事件键名。
    /// 由 LocalizationManager 派发、UILocalization 订阅；业务层也可用同一 key 订阅语言切换。
    /// 字符串值与原业务 EventConstKey.ChangeLanguage 完全一致，保证向后兼容。
    /// </summary>
    public static class LocalizationEventKeys
    {
        /// <summary>语言切换完成。无参。</summary>
        public const string ChangeLanguage = "ChangeLanguage";
    }
}
