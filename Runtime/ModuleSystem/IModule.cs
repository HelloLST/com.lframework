using Cysharp.Threading.Tasks;

namespace LFramework
{
    /// <summary>
    /// 框架模块接口 - 每个模块负责一个子系统(UI/Audio/Resource/Timer 等)
    /// 生命周期: InitializeAsync() -> Dispose()
    /// 需要逐帧更新的模块另行实现 <see cref="ITickable"/>（Phase 5 / 5.1 拆分），
    /// 纯初始化型模块不再被迫写空 Tick。
    /// </summary>
    public interface IModule
    {
        /// <summary>显示名(用于日志和诊断)</summary>
        string DisplayName { get; }

        /// <summary>
        /// 异步初始化。在 ModuleManager 创建实例后调用，按拓扑序依次执行。
        /// </summary>
        UniTask InitializeAsync();

        /// <summary>
        /// 框架关闭或运行期销毁时调用，逆序执行。
        /// </summary>
        void Dispose();
    }
}
