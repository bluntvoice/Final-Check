# ADR-0004：Windows packaging、版本与发布通道

- 状态：Accepted
- 日期：2026-09-12
- 适用版本：v0.1.0

## 决策

- 唯一产品版本源是根目录 `Directory.Build.props` 的 `Version`。目前尚未发布，源码基线为 `0.1.0-alpha.0`，以允许首次 beta 和 Stable 版本按严格 SemVer 顺序发布。产品版本线仍从 `0.1.0` 开始。
- SDK 从该值派生 Assembly/File Version 的数字部分及完整 Informational Version；关闭 Informational Version 的隐式 Git SHA 后缀。About 和侧栏读取 App assembly metadata，不另存版本常量。
- Windows x64 使用 Release、self-contained，不启用 trimming / NativeAOT。统一 PowerShell 脚本调用固定的 Velopack 1.2.0。
- Velopack NuGet 依赖仅存在于 Desktop；其生命周期入口必须先于 UI、DI 和数据库访问。核心四层不依赖 Velopack。
- 固定包 ID 为 `FinalCheck.App`，默认用户安装目录 `%LocalAppData%\FinalCheck.App`。现有用户数据目录 `%LocalAppData%\FinalCheck` 保持不变，禁止将数据库、Snapshot、日志、Backup 和设置放入安装目录或 `current`。
- Test / Prerelease / Stable 使用不同 metadata channel：`test` / `beta` / `win`。Internal Test 只存在于 Actions Artifact，不向 GitHub Releases 发布。
- updater 的完整 `.nupkg` 与 `releases.<channel>.json` 保留标准名称、版本、hash 和内容。安装包和 Portable 输出采用含版本的友好名称，上传专用 `assets.<channel>.json` 的对应文件名同步更新并验证。

## Spike 证据

2026-09-12 本机 `0.1.0-dev.1` 已生成 Setup、Portable、完整 nupkg、channel metadata 和 SHA-256。自动校验涵盖每个输出文件 hash、metadata 引用、包 ID/版本/channel、Portable 内 `sq.version`、About assembly Informational Version 与禁止打包的本地数据库。

publish 122.28 MiB，Setup 58.98 MiB，Portable 54.52 MiB。实际安装/启动/卸载验证与安装后体积由本任务 Phase 4 记录，不把 publish 大小当作实测安装大小。

## 边界和后续

本轮不实现软件内 updater UI 或 Release feed 查询，不创建真实 Tag / Release；发布工作流的行为与安全门禁参考 `bluntvoice/wechat-chat-summary`，不复制其 Tauri/NSIS 技术实现。代码签名和 macOS packaging 待后续独立验收。
