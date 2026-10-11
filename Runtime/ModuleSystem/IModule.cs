using Cysharp.Threading.Tasks;

namespace LFramework
{
    /// <summary>
    /// 框架模块接口 - 每个模块负责一个子系统(UI/Audio/Resource/Timer 等)
    /// 生命周期: InitializeAsync() -> Tick() x N -> Dispose()
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
        /// 每帧 Tick，由 ModuleManager.Update 统一驱动。默认空实现。
        /// </summary>
        void Tick(float deltaTime);

        /// <summary>
        /// 框架关闭或运行期销毁时调用，逆序执行。
        /// </summary>
        void Dispose();
    }
}
