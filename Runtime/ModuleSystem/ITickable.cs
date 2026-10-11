namespace LFramework
{
    /// <summary>
    /// 需要逐帧更新的模块实现此接口（Phase 5 / 5.1 自 IModule 拆分）。
    /// ModuleManager 只对 ITickable 调用 Tick——纯初始化型模块不再被迫写空方法，
    /// 派发循环也不必对空调用逐个 try-catch。
    /// </summary>
    public interface ITickable
    {
        /// <summary>每帧回调，由 MonoManager.Update → ModuleManager 统一驱动，全工程唯一逐帧出口。</summary>
        void Tick(float deltaTime);
    }
}
