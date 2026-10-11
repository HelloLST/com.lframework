# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.9.0] - 2026-10-11（进行中）

首个包形态版本。自 LFramework（XYFrameWorkDemo/Assets/Scripts/Core）整体迁入，迁入前已完成 Phase 1 止血修复：

### Fixed
- **ResourcesLoader**：修正引用计数所有权（`Load`=隐式持有 → `Release(path)` 归还；`AddRef/ReleaseRef` 显式钉住）；容量淘汰改为硬约束（跳过显式钉住的非 GameObject 资产）；清理循环加防自旋 guard —— 修复"缓存超限时自动清理协程无限循环卡死主线程"与"缓存无界增长"两个缺陷。
- **UIManager**：销毁面板时在 `ReleaseRef` 后追加 `Release(path)`，引用收支平账。
- **ModuleManifest**：拓扑排序初始队列按清单顺序入队；环回退按清单顺序 —— 修复"无依赖模块初始化顺序依赖字典枚举顺序（未定义行为）"。
- **关闭 Domain Reload 的静态残留**（本工程 EnterPlayModeOptions=3）：
  - `SingletonBase<T>`：进入 Play 清空静态实例（旧实例缓存假 null 资源）；
  - `GamePauseService`：暂停计数改 NonSerialized + 进 Play 清零（修复第二次进 Play 处于暂停态 / 计数写进资产）；
  - `ImageLocalization`：网络 Sprite 缓存进 Play 清空。

### Added
- `StaticStateAuditor`（Editor）：静态状态审计，扫描"持有 Unity 对象且无重置入口"的可写静态字段。
- `LFramework/Validate Module Manifest Asset`（Editor）：清单 typeName 编辑期校验。
- `link.xml`：保留 LFramework 程序集，防 IL2CPP 托管裁剪（清单反射解析依赖）。
- 回归测试：ResourcesLoader 引用计数/容量收紧 ×5、ModuleManifest 拓扑排序顺序稳定性 ×6。
