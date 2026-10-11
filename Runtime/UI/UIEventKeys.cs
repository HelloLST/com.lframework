namespace LFramework.UI
{
    /// <summary>
    /// UI 生命周期事件 key 的唯一出处（Phase 5 / 5.8）。
    /// 框架内部仍走 EventCenter 动态 key（生命周期广播属"动态 key"场景），
    /// 但 key 的拼装集中在此，杜绝散落在调用点的魔法字符串；
    /// 业务稳定事件请继续走 SO GameEventChannel（架构护栏强制）。
    /// </summary>
    public static class UIEventKeys
    {
        private const string Prefix = "UIManager.";

        /// <summary>面板完成打开（ShowExistingPanel / 新面板首开完成）</summary>
        public static string PanelOpen(string panelTypeName) => Prefix + "PanelOpen/" + panelTypeName;

        /// <summary>面板被隐藏（CloseInternal，实例保留）</summary>
        public static string PanelClose(string panelTypeName) => Prefix + "PanelClose/" + panelTypeName;

        /// <summary>面板被销毁（DestroyPanel）</summary>
        public static string PanelDestroy(string panelTypeName) => Prefix + "PanelDestroy/" + panelTypeName;
    }
}
