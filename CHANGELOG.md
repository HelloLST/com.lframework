# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.9.3] - 2026-11-11

> **试吃期第三轮至第七轮的集中发布。** 修复 16 项框架缺陷（含 17 项连带修复），
> 全部带回归测试并做过反向验证（临时还原缺陷 → 确认测试确实失败），累计测试基线
> EditMode 94 + PlayMode 87 = **181 条全绿**。
>
> 这批缺陷的共同特征：**测试全绿也发现不了**。没有一项是"必崩"的 P0，
> 但每一项都会在长会话、非默认配置、或错误路径上静默出问题 ——
> 内存只涨不落、配置写了不生效、失败没有任何痕迹。
>
> **无红线破坏性变更**，但有两处签名变化需要注意，见下方「升级注意」。

### 修复 · 模块与依赖

- **模块依赖只在排序时生效，运行时不校验**（C2）：`dependsOn` 此前**只参与拓扑排序**，不校验"依赖方是否真的初始化成功"。A 依赖 B、B 初始化失败时，A 照样初始化 —— 拿到的 `GetModule<B>()` 是 null，运行到第一次使用才炸，堆栈却指向 A 的业务代码，完全看不出根因在 B。
  修复：逐条校验 `dependsOn` 是否都在"已成功初始化"集合里，不满足则**跳过该模块**并记入 `SkippedModules`。跳过沿依赖链自动传播（A→B→C，C 挂了 B 和 A 都跳过），无需额外逻辑。
- **依赖校验位置在实例创建之后**（C2 连带）：若先 `AddComponent` 再判断"要不要初始化"，MonoBehaviour 的 `Awake` 已执行（可能访问了别的模块），副作用已经发生，此后再跳过只是没执行 `InitializeAsync` 而已。修复：校验前移到**创建实例之前**。
- **`GetModule<T>` 对"因依赖被跳过"给裸 null**（C2 连带）：修复为返回 null 时说明是被跳过的模块并附上原因，`BuildDependencyFailureReason` 区分"依赖初始化失败（带异常类型与消息）""依赖自身也被跳过""依赖不在清单中或未启用"三种情况，并点名是哪个模块。
- **`HasFailures` 不包含被跳过的模块**（C2 连带）：修复为同时覆盖 `FailedModules` 与 `SkippedModules`；结束汇总日志分别报告两者（失败的要查异常栈，被跳过的要查依赖链）。

### 修复 · UI

- **`OnHideAnimation` 全框架零调用**（B1）：`UIPanelBase` 暴露了关闭动画钩子，但没有任何地方调用它 —— 面板写了关闭动画永远不执行。修复：关闭/销毁两条路径都先播动画。
- **`CloseInternal` 声称含动画实际同步返回**（B2）：修复为真正 `await OnHideAnimation`，事件顺序改为 隐藏 → 动画 → 广播。
- **`DestroyPanel` 在 `UIManager.Init()` 前 NRE**：修复为 `_panelDic` 判空，新增 `PanelDicOrEmpty`。
- **`Open<T>` 失败时调用方拿不到信号**（B4）：`Open<T>()` 是 `void` + `.Forget()`，找不到预制体时只打一条 Error —— 调用方完全不知道失败了，`Forget` 还不会抛。症状是"调了 Open，界面没出来，但没有任何报错指向我的代码"。
  修复：新增 `TryOpen<T>()` 立即返回成败（同步路径在启动流程前**预检预制体**，因此能在同一帧给出结果），失败原因经新增的 `LastOpenError` 带出并点名是哪个面板。`Open<T>` 签名不动。

### 修复 · 资源

- **`LoadAll` 完全绕过缓存与引用计数**（A1）：旧实现直接 `return Resources.LoadAll<T>(path)` —— 资源不进缓存、不计引用。业务拿到一批图后，这批资源在框架眼里根本不存在：`IsCached` 恒 false、LRU 看不见、`ReleaseUnused` 管不到、容量策略也摸不着。想按容量回收回收不掉，只能靠 `Resources.UnloadUnusedAssets` 全量兜底（代价远大于定向回收）。
  修复：逐个登记进缓存并各计一次隐式持有，由新增的 `ReleaseAllFrom(folder)` 分组归还。
- **递归加载的路径推导不可靠**（A1 连带）：`Resources.LoadAll` 是**递归**的，而运行时拿不到资源的真实子路径（`AssetDatabase.GetAssetPath` 打包后不可用）。靠 `folder/资源名` 推导会让递归目录下的条目归还时漏掉。
  修复：`LoadAll` 额外做**分组登记**（目录 → 本次登记过的 key 列表），`ReleaseAllFrom` 照单归还，与路径推导彻底解耦。
- **`Release(path)` 无成对校验**（A4）：对未加载路径静默忽略、重复 Release 也不报错。业务漏 Release 或重复 Release 都无痕迹，只能靠 `GetCacheCount` 猜。修复：拆出 `ImplicitCount` 独立计数 —— 缓存命中计持有；重复 Release / 未缓存路径显式告警；重复 Release 不再吃掉显式引用。
- **`ResourceHandle.Get<T>()` 在新计数语义下泄漏**（A4 连带）：修复为新增 `TryPeek<T>` / `ResourcesManager.Peek<T>`，`Get` 改为只读缓存不计数。
- **`LoadAsync` 并发等待失败无告警**（A3）：只有"首个加载者"那条路径会告警，等待者静默拿到 null。修复：等待者路径补告警与隐式持有登记。

### 修复 · 对象池

- **超容量销毁跳过 `OnDespawn`**（F1）：`Despawn` 在 `capacity` 超限时直接 `Destroy(go)`，跳过了 `IPoolable.OnDespawn` —— 对象拿到 `OnSpawn` 却永远等不到配对的 `OnDespawn`，靠它退订事件/归还外部引用的组件会静默泄漏，且只在"池达到容量上限"这种非默认状态下才发生。修复：先 `InvokeOnDespawn()` 再 `Destroy`（顺序不能反，否则回调里访问自己的 GameObject 已是假 null），`Clear` 对**使用中**对象的销毁同样补调。
- **反向陷阱：空闲对象被重复调 `OnDespawn`**（F1 连带）：若为了"对称"给 `Clear`/`Trim` 里的空闲对象也补调，就变成"一次 `OnSpawn` 配两次 `OnDespawn`"。修复：只给**使用中**对象补调。
- **`DespawnImmediate` 找不到池时静默销毁**（F2）：旧实现 `Destroy(go)` 后返回 `void`，调用方还以为是回收成功、下次 `Spawn` 会复用。修复：返回 `bool`（是否成功回收到池），并在销毁前告警说明"已直接销毁、不会被复用"。
- **`PoolItem` 每次 `GetComponents<IPoolable>()`**（F4）：该方法**每次新分配数组**，即使一个 `IPoolable` 都没有。Spawn/Despawn 是子弹、飘字、特效的热路径，每秒数百次即构成持续 GC 压力（表现为周期性卡顿，不报错，只能靠 Profiler 看出来）。修复：首次收集后缓存数组，回调改索引循环并跳过假 null；新增 `InvalidatePoolableCache()` 供运行期增删组件后手动失效。

### 修复 · 日志

- **`MaxFileSizeBytes` 从未生效**（D4）：`LogSettingsAsset.MaxFileSizeMB` → `LogManager.MaxFileSizeBytes`，但 `FileLogWriter` 从来不做按大小切分 —— 一个在设置面板里可调、写进 SO 资产、看起来完全生效的开关，实际零作用。后果是日志文件无限增长，移动端会把存储写满。修复：`FileLogWriter` 补按大小轮转（`.1`~`.N` 备份，份数受 `SaveDays` 约束）。
- **`LogManager.InitializeWriters` 不可重入**（D4 连带）：二次调用会不断往 `_writers` 里追加，导致同一条日志写多遍、旧文件句柄泄漏。修复：重建前先 Flush+Close 旧 writer；新增公开 `ReinitializeWriters()`，运行时改配置即可生效。
- **`FileLogWriter.Close` 在锁内调 `Write` 语义混乱**：修复为收尾标记走 `WriteLineDirect`，不经过缓冲逻辑。
- **`LogUploader` 待发队列无上限**（D3）：`_pendingLogs` 是无限 Queue。生产端（每条超阈值日志）远快于消费端（服务器不可达时原地退避重试），队列只涨不落 —— 服务器挂了 + 业务疯狂报错的场景下，日志系统自己会成为压死进程的最后一根稻草。修复：抽出 `LogUploadQueue` —— 超限丢**最旧的**（保留最近现场）、丢弃计数可观测。
- **上传失败批次被重排到队尾**（D3 连带）：失败批次是最旧的现场，放队尾会被新日志反复挤到后面，极端情况下永远发不出去。修复为 `RequeueAtHead`，回到队首且保持批次内部顺序。
- **`LogManager` 的 writer 缺"按需建立"路径**（D1）：writer 只在 `Awake` 里建立。若 `LogManager` 被走到了但 `Awake` 没执行（EditMode 里 `AddComponent` 不触发 `Awake`；或模块清单漏配后有人手工 `new`），`_writers` 就是空的 —— 所有日志**静默消失**。修复：`WriteToWriters` 发现 writer 为空时**按需建立**一次，`_writersEnsured` 约束只尝试一次。

### 修复 · 本地化

- **网络 Sprite 缓存只进不出**（H3）：`_netSpriteCache` 是静态字典，每下载一张图就多一条，进程不退出永不释放。Sprite / Texture2D 是运行时创建的 Unity 对象，**GC 收不走，必须显式 Destroy** 才还显存。长会话（多语言切图、活动页反复打开）下内存只涨不落且无报错。
  修复：新增 `NetSpriteCache` —— 条目带**持有数**与 LRU 时钟；超过 `MaxCount`（默认 32）时只淘汰**持有数为 0** 的最久未用项，淘汰时真正 `Destroy` Sprite 与 Texture2D。持有数 > 0 的条目永不被淘汰（宁可短暂超限，也不能把正在显示的图销毁掉）。
- **异步结果过期仍贴图**（H3 连带）：`LoadNet` 在 `await` 之后不校验自身状态，语言切换导致 url 已变、或组件已销毁时，旧结果仍会写进 `Image.sprite`，并让该 url 的持有数永久 +1。修复：加 `m_RequestGeneration` 请求代号，不符则丢弃结果并归还持有。
- **`Register` 与 `Acquire` 分离导致自淘汰竞态**（H3 连带）：`Register` 以持有数 0 入库并立刻 `Trim`，此时新条目恰好是唯一的「无人持有」项，会被自己触发的淘汰清掉。修复：新增原子的 `RegisterAndHold`。

### 新增 API

- `ModuleManager.SkippedModules`：因依赖不可用被跳过的模块及原因（C2）。
- `ModuleManager.HasFailures` 语义扩展：现在也包含被跳过的模块。
- `UIManager.TryOpen<T>()` / `UIManager.LastOpenError`：同步路径的显式成败版（B4）。
- `ResourcesManager.ReleaseAllFrom(folder)` / `ResourcesLoader.MakeAssetCachePath(...)`：分组归还 `LoadAll` 的持有（A1）。
- `NetSpriteCache`（公开类型）：`MaxCount` / `Count` / `TryPeek` / `TryAcquire` / `Acquire` / `Release` / `Register` / `RegisterAndHold` / `GetHolds` / `Clear` / `Trim`。业务可在切场景或内存告警时调 `Clear()` 主动压显存。
- `PoolItem.InvalidatePoolableCache()`：运行期增删 `IPoolable` 组件后使组件缓存失效。
- `LogUploadQueue`（公开类型）：待发队列上限策略的独立实现。
- `LogUploader.MaxPendingLogs` / `LogUploader.GetDroppedCount()`：队列上限可配、丢弃量可观测。
- `LogManager.ReinitializeWriters()`：运行时改日志配置后重建 writer，不必重启。

### 升级注意（两处签名变化）

1. **`ObjectPoolsManager.DespawnImmediate` 与 `GameObject.DespawnImmediate` 现在返回 `bool`**（F2）。
   直接调用无需改动；若写成 `Action<GameObject>` 委托或方法组转换，需要改成 lambda。
2. **`IResourceLoader` 新增 `ReleaseAllFrom(string)` 成员**（A1）。
   自定义后端（非内置 `ResourcesLoader` / `AddressablesLoader`）需要实现它，返回 0 即可。

### 行为变更提示

- **模块依赖现在是硬约束**：`dependsOn` 写了但依赖方初始化失败时，本模块会被**跳过**（旧版是照常初始化）。
  若升级后看到某些模块被跳过，说明依赖链上确实有坏模块 —— 请检查 `SkippedModules`。
- **`LoadAll` 现在产生持有**：必须通过 `ReleaseAllFrom` 或逐个 `Release` 归还（旧版不产生任何持有）。
  迁移最省事的写法：`ResourcesManager.Instance.ReleaseAllFrom(原 LoadAll 用的目录)`。

### 台账纠错

- **E1「`Subscribe` 令牌在重复订阅时只退订一次」—— 误记，非缺陷。**
  `Delegate.Remove` 的语义正是「移除**最后一个**匹配项」，两个令牌依次 `Dispose` 会摘两次、正好摘干净。
  已补 `DelegateRemoveSemanticsTests`（3 条）固化结论。

### 测试基线

EditMode **94** + PlayMode **87** = **181** 条用例全绿，日志扫描无运行期错误。

本轮新增：`ModuleDependencyTests` ×9、`NetSpriteCacheTests` ×12、`LogUploadQueueTests` ×11、
`PoolItemPoolableCacheTests` ×6、`PoolLifecycleSymmetryTests` ×6、`LoadAllCacheTests` ×9、
`FileLogWriterRotationTests` ×6、`UIPanelHideAnimationTests` ×4、`ResourceHandleTests` ×3、
`LogWriterOnDemandTests` ×5、`UIOpenFailureTests` ×2、`DelegateRemoveSemanticsTests` ×3。
## [0.9.2] - 2026-11-11

> 补齐 Phase 7 遗留的两个观察项：事件派发的异常隔离、日志模块的会话重置。
> 两者都是"不会立刻炸、但会在特定场景静默失效"的类型。

### Fixed

- **EventCenter 监听器异常不隔离**：多播委托直接 Invoke()，任一监听器抛异常会**中断后续全部监听器**，且异常冒泡给触发方 —— 一个 UI 监听器写错就能让整条业务事件链断掉。这与 MonoManager 已有的隔离策略不一致。改为取 GetInvocationList() 逐个调用，单个监听器异常就地拦截并打 Error 日志（带事件名便于定位）。代价是每次派发多一次 GetInvocationList 分配，事件派发非每帧热路径，可接受。
- **LogManager 静态状态无会话重置**（关域重载场景）：
  - _startupBuffer 残留 —— 上一会话没回放的日志会混进新会话首屏，并占容量把本会话的启动日志挤掉；
  - OnLogReceived 残留 —— 上一会话已销毁对象的订阅仍在委托上，本会话首次派发即撞 Unity 假 null；
  - LogUploader._pendingLogs 残留 —— 上一会话未发出的日志会在本会话再传一遍；
  - LogUploader._isUploading 残留为 	rue —— 本会话 StartUpload 直接 return，**上传功能永久失效且无任何提示**；_retryDelay / _consecutiveFailures 亦会带进新会话。
  
  修复：LogManager 补 [RuntimeInitializeOnLoadMethod(SubsystemRegistration)] 复位上述状态，并新增 LogUploader.ResetForNewSession()。_sanitizePatterns / _sanitizeCombinedRegex **刻意不清理** —— 纯函数性缓存（正则对象，不持有 Unity 对象），重建反而浪费。LogManager 是非泛型类型，引擎回调正常触发，无需走 RuntimeSessionReset。

### Added
- 回归测试：EventCenterIsolationTests ×5（抛异常的监听器不阻断后续、带参重载同样隔离、多个异常各自隔离、无监听器安全、日志带事件名）。

## [0.9.1] - 2026-11-11

> **试吃期第一轮修复，建议所有使用 0.9.0 的工程升级。**
>
> 0.9.0 发布后经"回读源码 + 给测试脚本补日志校验"发现 4 个运行时缺陷，
> 其中 1 个为 **P0**：模块销毁时必然抛 `NullReferenceException`。
> 这些缺陷此前**全部被测试绿灯掩盖**（异常被 `try/catch` 吞进日志，而旧测试脚本不看日志），
> 修复后同时补上了对应回归测试与日志错误护栏。

### Fixed

**P0 — 模块实例获取（`ModuleManager`）**

- **产出"幽灵实例"，Dispose 时必崩**（回归测试 `ModuleManagerTests` ×5 抓到）：`CreateInstance` 用 `type.GetProperty("Instance", Public|Static)` 找懒加载单例入口，而 .NET **不会沿继承链返回声明在泛型基类上的静态属性**。`ResourcesManager` / `LocalizationManager`（均继承 `SingletonBase<T>`）因此全部落进 `Activator.CreateInstance` 回退分支，造出 **`Init()` 从未执行**的实例：它与 `Xxx.Instance` 不是同一个对象、`_loader` 为 null。业务侧全走 `Instance` 所以运行时表现正常，直到销毁时才炸 —— `ModuleManager.OnDestroy → ResourcesManager.Dispose → ReleaseAll → NullReferenceException`。**修复**：逐级 `BaseType` 查找静态 `Instance`（校验返回类型可赋值给模块类型），用 `Delegate.CreateDelegate` 缓存获取器。
- **记录到"即将自我销毁的重复实例"**：若 `UIManager.Instance` 之类在模块初始化之前已被访问，`AddComponent` 会造出第二个实例，它在 `Awake` 里立刻 `Destroy` 自己，而 `_instancesByTypeName` 记下的正是这个将死的对象。**修复**：AddComponent 前后各探一次继承链上的 `TryGetExisting`，已有存活单例则直接沿用。
- **反射回退路径静默跳过 `Init()`**：没有静态 `Instance` 的类型仍走 `Activator`（行为保持不变，向后兼容），但现在会打 Warning 明确提示"`Init()` 不会被执行，请把初始化放进 `InitializeAsync()`"。

**资源**

- **`ResourcesManager._loader` 可能为 null**：新增惰性 `Loader` 访问点，任何取用路径都拿得到可用加载器；`Init()` 仍照常赋值。即使将来再有代码绕过 `Instance` 拿到对象，`ReleaseAll` / `Load` 也不会再抛 NRE。

**日志**

- **启动缓冲满时静默丢日志**：缓冲达上限（256）后原实现是"丢弃最旧一条"，但若本会话里 `LogManager` 永远不会被创建（典型：EditMode 测试中 `AddComponent` 不触发 `Awake`；或模块清单里漏配 `LogManager`），缓冲永不回放，日志会**整段消失**，排查时看起来"什么都没发生"。**修复**：缓冲满时溢出到 Unity 控制台（`WriteFallbackToUnityConsole`），宁可吵也不丢。

**会话重置（关域重载 DR-off）**

- **开放泛型类型上的会话重置从未生效**（Phase 7 完整性测试发现）：引擎不会为闭合泛型类型调用声明在开放泛型类型中的 `[RuntimeInitializeOnLoadMethod]`（已实测 Unity 2022.3.62f3c1），导致 DR-off 下 `SingletonBase<T>` 静态实例、`ScriptableConfigCache<T>` 配置缓存、`UnitySingleton<T>` 的 `_instance`/`IsExisted`/`_isQuitting` 全部跨 Play 会话残留——**0.9.0 里"进入 Play 自动重置"的声明对这三类不成立**。新增非泛型 `RuntimeSessionReset`（Runtime/Singleton）作为引擎回调入口：三个泛型类在静态构造函数里注册重置委托（注册时立即执行一次），每次进 Play 遍历重置；泛型类型上的旧特性仅保留作静态审计标记。

**编辑器护栏**

- **构建拦截开关不持久**（Phase 7.4）：`FailBuildOnHardViolation` 由内存静态字段改为按工程的 `EditorPrefs`（key 含工程路径），菜单勾选跨编译/重启保留；默认仍为关闭，不改变现有出包行为。
- **扫描报告落盘**（Phase 7.4）：全量扫描与构建拦截共用 `ScanProject()`，结果写工程根 `Artifacts/ArchitectureGuardrail/report.json`（时间戳、开关、文件数、错误/提醒/读取失败计数与逐条违规），供 CI 消费；读取失败的文件计入 `ReadFailures`，开启拦截时同样中止构建；空工程（无 `Assets/Scripts`）产出零文件报告而非抛异常；测试程序集目录（`/Tests/`、`/TestsPlayMode/`）纳入排除清单。

### Added
- `RuntimeSessionReset`（Runtime/Singleton）：非泛型会话重置中枢，替代在开放泛型类型上无效的引擎回调。
- 回归测试：`ModuleManagerTests` ×5（泛型基类静态属性前提、`GetModule` 返回真实例、`Dispose` 不抛、沿用既有单例、反射回退边界）。

### Notes
- 本次未改动任何公开 API 签名，升级保持源码兼容。
- 测试基线（demo 工程）：EditMode 62 + PlayMode 27 = **89 全绿**。

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
