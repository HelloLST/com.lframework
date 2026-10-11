using System;

namespace LFramework.UI
{
    /// <summary>
    /// 标记面板在 Close 时不销毁，仅 SetActive(false)，下次 Open 时复用实例
    /// 适用于高频打开/关闭、初始化开销较大、希望保留运行时状态的面板
    /// 未标注的面板在 Close 时会被销毁，并释放对应预制体的引用
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
    public sealed class UIKeepAliveAttribute : Attribute
    {
    }
}
