# LFramework Core

个人开发用轻量级 Unity 客户端框架内核。机制上提、数据下沉：框架不认识任何业务名词，业务通过接口 / SO / 清单注入。

| | |
|---|---|
| 引擎 | Unity 2022.3（LTS） |
| 异步 | [UniTask](https://github.com/Cysharp/UniTask)（前置依赖，见下） |
| 安装方式 | UPM git URL，tag 锁版本 |

## 安装

**第一步：安装 UniTask**（二选一）

```jsonc
// Packages/manifest.json —— 方式 A：git URL
"com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git"
```

```jsonc
// 方式 B：OpenUPM scoped registry
"scopedRegistries": [
  { "name": "OpenUPM", "url": "https://package.openupm.com",
    "scopes": ["com.cysharp.unitask"] }
],
"dependencies": { "com.cysharp.unitask": "2.5.10" }
```

**第二步：安装本包**

```jsonc
// Packages/manifest.json
"com.lframework.core": "https://github.com/HelloLST/com.lframework.git#v0.9.0"
```

> 本地开发调试可用 file 路径引用：`"com.lframework.core": "file:../../com.lframework"`

## 模块总览

| 模块 | 入口 | 说明 |
|---|---|---|
| 模块系统 | `ModuleManager` + `ModuleManifest` SO | 清单驱动 + 拓扑排序；编辑期校验菜单 `LFramework/Validate Module Manifest Asset` |
| UI | `UIManager` / `UIPanelBase` | 面板栈 / 层级（`[UILayer]`）/ 复用（`[UIKeepAlive]`）/ 命名约定自动绑定 / 数据绑定（`BindableProperty<T>` + `UIPanelBase.Bind`，`OnUIDestroy` 自动退订） |
| 资源 | `ResourcesManager` | 引用计数 + LRU；契约：`Load` 隐式持有 → `Release(path)` 归还；钉住资源用 `Pin<T>()` 取 `IResourceHandle`，Dispose 即归还（`AddRef/ReleaseRef` 已标 `[Obsolete]`，保留一个版本周期） |
| 对象池 | `ObjectPoolsManager` | 池化 + 空闲裁剪 + 假 null 清扫 |
| 事件 | `EventCenter` / SO `GameEventChannel` | 动态 key 走 EventCenter；稳定业务事件走 SO 通道（护栏强制） |
| SO 架构层 | `ScriptableVariable` / `RuntimeSet` / `GamePauseService` | 共享变量 / 运行时集合 / 暂停唯一出口（timeScale 只准走它） |
| 日志 | `LogManager` | 级别 / 文件双缓冲 / 敏感信息脱敏 / 远程上传；启动期日志先入缓冲、`Instance` 就绪后回放 |
| 本地化 | `LocalizationManager` | Excel → JSON → 字典，切语言广播自动刷新 |
| 音频 / 计时器 / 场景 | `AudioManager` / `TimerMgr` / `LoadSceneManager` | |
| 设备接入 | `UDPDevice` | **定位：局域网设备 JSON 接入适配层，不是通用网络层**（无重连/心跳/校验） |
| 调试 | `Tools/LFramework/` | 架构护栏 / 静态状态审计 / 清单校验 |

## 编辑器菜单

- `Tools/LFramework/架构校验/全量扫描业务层` —— 反模式扫描（`// ARCH_EXEMPT` 行内豁免）
- `Tools/LFramework/架构校验/静态状态审计` —— Domain Reload 关闭下的静态残留风险扫描
- `LFramework/Create Module Manifest Asset` / `LFramework/Validate Module Manifest Asset`

## 文档

模块 API 与示例见 [`Documentation~/使用说明.md`](Documentation~/使用说明.md)。

## License

MIT
