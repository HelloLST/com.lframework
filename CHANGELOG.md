# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.9.0] - 2026-10-11（进行中）

首个包形态版本。自 LFramework（XYFrameWorkDemo/Assets/Scripts/Core）整体迁入，迁入前已完成 Phase 1 止血修复，迁入后完成 Phase 5 第一批内核打磨：

### Fixed
- **ResourcesLoader**：修正引用计数所有权（`Load`=隐式持有 → `Release(path)` 归还；`AddRef/ReleaseRef` 显式钉住）；容量淘汰改为硬约束（跳过显式钉住的非 GameObject 资产）；清理循环加防自旋 guard —— 修复"缓存超限时自动清理协程无限循环卡死主线程"与"缓存无界增长"两个缺陷。
- **UIManager**：销毁面板时在 `ReleaseRef` 后追加 `Release(path)`，引用收支平账。
- **ModuleManifest**：拓扑排序初始队列按清单顺序入队；环回退按清单顺序 —— 修复"无依赖模块初始化顺序依赖字典枚举顺序（未定义行为）"。
- **关闭 Domain Reload 的静态残留**（本工程 EnterPlayModeOptions=3）：
  - `SingletonBase<T>`：进入 Play 清空静态实例（旧实例缓存假 null 资源）；
  - `GamePauseService`：暂停计数改 NonSerialized + 进 Play 清零（修复第二次进 Play 处于暂停态 / 计数写进资产）；
  - `ImageLocalization`：网络 Sprite 缓存进 Play 清空。
- **MonoManager**：监听器从多播委托改为 List 管理 —— 重复添加去重、单监听器异常隔离（修复前一个监听器抛异常中断后续全部）、倒序遍历允许回调中安全自移除。

### Changed（Phase 5 第一批）
- **`IModule` 拆出 `ITickable`**：逐帧更新成为可选能力，纯初始化型模块不再被迫写空 Tick；`ModuleManager` 只对 `ITickable` 派发。`TimerMgr` / `ObjectPoolsManager` / `LogManager`（刷盘 Tick）实现 `ITickable`，删除各自挂载的 `Update()`——全工程逐帧逻辑归一到 MonoManager 单一出口。
- **Core 零协程化**：ResourcesLoader 清理循环与 UnloadUnusedAssets、AudioManager SFX 回收（CTS 可取消，修复重复播放竞态的取消语义）、ObjectPoolsManager 延迟回收、LogUploader 上传链（指数退避原地重试）全部改为 UniTask 驱动；删除 `LoadSceneManager` 的协程 `[Obsolete]` 桥接；`ResourcesLoader` 与 MonoManager 彻底解耦。

### Added
- `StaticStateAuditor`（Editor）：静态状态审计，扫描"持有 Unity 对象且无重置入口"的可写静态字段。
- `LFramework/Validate Module Manifest Asset`（Editor）：清单 typeName 编辑期校验。
- `link.xml`：保留 LFramework 程序集，防 IL2CPP 托管裁剪（清单反射解析依赖）。
- 回归测试：ResourcesLoader 引用计数/容量收紧 ×5、ModuleManifest 拓扑排序顺序稳定性 ×6。
