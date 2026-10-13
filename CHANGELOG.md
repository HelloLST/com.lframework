# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed
- **开放泛型类型上的会话重置从未生效**（Phase 7 完整性测试发现）：引擎不会为闭合泛型类型调用声明在开放泛型类型中的 `[RuntimeInitializeOnLoadMethod]`（已实测 Unity 2022.3.62f3c1），导致关域重载（DR-off）下 `SingletonBase<T>` 静态实例、`ScriptableConfigCache<T>` 配置缓存、`UnitySingleton<T>` 的 `_instance`/`IsExisted`/`_isQuitting` 全部跨 Play 会话残留——0.9.0 里"进入 Play 自动重置"的声明对这三类不成立。新增非泛型 `RuntimeSessionReset`（Runtime/Singleton）作为引擎回调入口：三个泛型类在静态构造函数里注册重置委托（注册时立即执行一次），每次进 Play 遍历重置；泛型类型上的旧特性仅保留作静态审计标记。demo 仓库新增真实双会话完整性测试（EnterPlayMode/ExitPlayMode 两次、关 Domain Reload）证明修复。
- **ArchitectureGuardrail 构建拦截开关不持久**（Phase 7.4）：`FailBuildOnHardViolation` 由内存静态字段改为按工程的 `EditorPrefs`（key 含工程路径），菜单勾选跨编译/重启保留；默认仍为关闭，不改变现有出包行为。
- **ArchitectureGuardrail 扫描报告落盘**（Phase 7.4）：全量扫描与构建拦截共用 `ScanProject()`，结果写工程根 `Artifacts/ArchitectureGuardrail/report.json`（时间戳、开关、文件数、错误/提醒/读取失败计数与逐条违规），供 CI 消费；读取失败的文件计入 `ReadFailures`，开启拦截时同样中止构建；空工程（无 `Assets/Scripts`）产出零文件报告而非抛异常；测试程序集目录（`/Tests/`、`/TestsPlayMode/`）纳入排除清单。
## [0.9.0] - 2026-10-12

首个包形态版本，已打 tag `v0.9.0`。自 LFramework（XYFrameWorkDemo/Assets/Scripts/Core）整体迁入：迁入前完成 Phase 1 止血修复；迁入后完成 Phase 5 第一批（5.1–5.3，提交 `5c486eb`）、第二批（5.4 / 5.6 / 5.8，提交 `501236f`）、5.5（提交 `313a527`）与 5.7 前半（提交 `fb0689c`）的内核打磨，以及 Phase 6 的文档产出（`Documentation~/`）。

> 本版定位为**试吃版**：由 `XYFrameWorkDemo` 先吃两周毛刺，后续修复走 `0.9.x`，稳定后再升 `1.0.0`。

### Fixed
- **Package manifest 缺内置模块依赖**（空工程实测发现）：`package.json` 未声明框架代码用到的 Unity 内置模块，导致**在全新工程里整个包编译失败**（`AudioManager` 的 `AudioSource`/`AudioClip` 等 45 处 `CS1069`）。已补 `com.unity.modules.audio / animation / jsonserialize / unitywebrequest / imgui` 五项依赖。demo 工程的 manifest 恰好带了一长串 `com.unity.modules.*`，掩盖了这个问题。
- **ResourcesLoader**：修正引用计数所有权（`Load`=隐式持有 → `Release(path)` 归还；`AddRef/ReleaseRef` 显式钉住）；容量淘汰改为硬约束（跳过显式钉住的非 GameObject 资产）；清理循环加防自旋 guard —— 修复"缓存超限时自动清理协程无限循环卡死主线程"与"缓存无界增长"两个缺陷。
- **UIManager**：销毁面板时在 `ReleaseRef` 后追加 `Release(path)`，引用收支平账。
- **ModuleManifest**：拓扑排序初始队列按清单顺序入队；环回退按清单顺序 —— 修复"无依赖模块初始化顺序依赖字典枚举顺序（未定义行为）"。
- **关闭 Domain Reload 的静态残留**（本工程 EnterPlayModeOptions=3）：
  - `SingletonBase<T>`：进入 Play 清空静态实例（旧实例缓存假 null 资源）；
  - `GamePauseService`：暂停计数改 NonSerialized + 进 Play 清零（修复第二次进 Play 处于暂停态 / 计数写进资产）；
  - `ImageLocalization`：网络 Sprite 缓存进 Play 清空。
- **MonoManager**：监听器从多播委托改为 List 管理 —— 重复添加去重、单监听器异常隔离（修复前一个监听器抛异常中断后续全部）、倒序遍历允许回调中安全自移除。
- **启动期日志丢失**（Phase 5 / 5.4）：`LogManager` 不再被日志调用懒创建实例；`Instance` 就绪前的日志先进静态缓冲，模块初始化后回放。
- **`UIManager` 静态残留（关域重载关闭时）**（Phase 5.5）：补 `RuntimeInitializeOnLoadMethod` 静态重置，清掉 Phase 1 静态审计遗留的最后一项高危缓存。
- **`LogSettingsManager` 迁移残留**（Phase 5.5）：修掉改用 `ScriptableConfigCache` 后遗留的 `_cachedAsset` 引用（迁移期编译错误，已修）。
- **`LogManager` 残留 `Update()`**（Phase 5.1 漏改）：刷盘逻辑同时挂在 Unity `Update()` 与 `ITickable.Tick()` 上，每帧执行两次。`LogManager` 继承自 `UnitySingleton`（MonoBehaviour），所以 `Update()` 确实被 Unity 调用，不是死代码。删除 `Update()`，逐帧逻辑真正回到 MonoManager → ModuleManager 单一出口（使上面的 5.1 声明名副其实）。

### Changed（Phase 5 第一批 · `5c486eb`）
- **`IModule` 拆出 `ITickable`**：逐帧更新成为可选能力，纯初始化型模块不再被迫写空 Tick；`ModuleManager` 只对 `ITickable` 派发。`TimerMgr` / `ObjectPoolsManager` / `LogManager`（刷盘 Tick）实现 `ITickable`，删除各自挂载的 `Update()`——全工程逐帧逻辑归一到 MonoManager 单一出口。
- **Core 零协程化**：ResourcesLoader 清理循环与 UnloadUnusedAssets、AudioManager SFX 回收（CTS 可取消，修复重复播放竞态的取消语义）、ObjectPoolsManager 延迟回收、LogUploader 上传链（指数退避原地重试）全部改为 UniTask 驱动；删除 `LoadSceneManager` 的协程 `[Obsolete]` 桥接；`ResourcesLoader` 与 MonoManager 彻底解耦。

### Changed（Phase 5 第二批 · `501236f`）
- **日志管道归一（5.4）**：Runtime 内 103 处 `Debug.Log*` 全部改走 `LogManager`（日志模块自身保留 `Debug` 作为最终落地写入器，属有意保留）；8 处两参 `Debug.Log(msg, context)` 改为名字标签形式。
- **资源 handle 化第一步（5.6）**：新增 `IResourceHandle` / `ResourcesManager.Pin<T>()`（获取即钉住、Dispose 即归还）；`UIManager` 面板预制体改用句柄持有。**`ResourcesManager.AddRef` / `ReleaseRef` 已标 `[Obsolete]`**，保留一个版本周期后移除；`Release(path)` 是隐式持有的归还路径，**不在废弃之列**，`IResourceLoader.AddRef/ReleaseRef` 作为资源后端 SPI 亦不废弃。
- **UI 事件 key 类型化（5.8）**：`UIManager.PanelOpen/{Type}` 这类运行时拼字符串收敛到 `UIEventKeys.PanelOpen/PanelClose/PanelDestroy`，拼写错误从运行期提前到编译期。
- **`UnitySingleton.TryGetExisting`**：新增不创建实例的探针式访问器，供基础设施层查询单例而不触发惰性构造。

### Changed（Phase 5.5 · `313a527`）
- **新增 `ScriptableConfigCache<T>`**（Runtime/Config）：把静态缓存 + 惰性加载 + 关域重载（DR-off）重置收成一个泛型基类；`UISettings`、`LogSettingsManager`、`ArchitectureGuardrailSettings` 三份同构样板迁入，各自只剩资源路径与默认值。

### Changed（Phase 5.7 · 吸收自 XyFrame）
- **吸收 `BindableProperty<T>`**（Runtime/UI）：值真正变化时触发 `Changed`（用 `EqualityComparer<T>.Default` 比较，同值赋值不触发），并提供 `ForceNotify()` 供初始化后强制同步。属 D6 冻结的“只吸收三件”第一件；**未搬运** XyFrame 的 Controller / View / Model 三件套，避免形成第三套 UI 范式（D3）。
- **`UIPanelBase` 绑定钩子**：新增 `Bind<T>(property, handler, invokeImmediately)` / `Unbind<T>(...)` / `UnbindAll()`；经 `Bind` 建立的订阅会被登记，并在 `OnUIDestroy()` 里自动退订，面板不必手写 `-=`。**解绑点刻意选销毁而非 `OnUIDisable`** —— 保活面板（`[UIKeepAlive]`）隐藏时不销毁，绑定点放在 Disable 会把订阅弄丢。

### Added
- `StaticStateAuditor`（Editor）：静态状态审计，扫描"持有 Unity 对象且无重置入口"的可写静态字段。
- `LFramework/Validate Module Manifest Asset`（Editor）：清单 typeName 编辑期校验。
- `link.xml`：保留 LFramework 程序集，防 IL2CPP 托管裁剪（清单反射解析依赖）。
- `ScriptableConfigCache<T>`（Runtime/Config）：配置资产的静态缓存 / 惰性加载 / 关域重载重置泛型基类。
- `IResourceHandle` + `ResourcesManager.Pin<T>()`（Runtime/Res）：资源句柄 API（获取即钉住、Dispose 即归还）。
- `UIEventKeys`（Runtime/UI）：UI 生命周期事件 key 集中化。
- `UnitySingleton.TryGetExisting`（Runtime/Singleton）：不创建实例的单例探针。
- `BindableProperty<T>`（Runtime/UI）：可绑定属性，赋值即通知（Phase 5 / 5.7）。
- `UIPanelBase.Bind / Unbind / UnbindAll`（Runtime/UI）：面板侧数据绑定钩子，面板销毁自动退订。
- 回归测试：ResourcesLoader 引用计数/容量收紧 ×5、ModuleManifest 拓扑排序顺序稳定性 ×6。
- 测试：`BindablePropertyTests` ×7（同值不触发、`ForceNotify`、`invokeImmediately` 初值同步、`UnbindAll` 生效、`OnUIDestroy` 自动退订）；全量 69/69 PlayMode 绿。
- `Documentation~/快速上手.md`（Phase 6）：面向新工程的从零上手 —— 安装（UniTask + 本包）→ 模块清单与 `ModuleManager` → 日志 / 资源 / UI 三条链路 → 常见坑表。README 的「文档」一节改为指向它。
- `Documentation~/使用说明.md`（Phase 6）：参考手册的**包版**。由 demo 仓库那份剔除 demo 专属章节（场景与入口、启动链路、构建、测试基线、业务配置、demo 示例、已知问题清单）与 demo 表格行得到；框架章节逐字保留，仅改写了章节内的 demo 引用。章节编号刻意不动（重编号会打断十余处正文互引），目录只列出存留章节。两份文档互相注明关系与「框架章节两边同步」的规则。
