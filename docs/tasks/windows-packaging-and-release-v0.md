# Windows Packaging and Release v0

- 适用版本：Final Check v0.1.0
- 状态：IN PROGRESS
- 范围：Windows x64 self-contained 打包、Internal Test Build、正式 Release 工作流及发布文档
- 不在范围：Updater UI、代码签名、macOS 安装包、真实 Stable Release

## 执行纪律

本任务遵循 `AGENTS.md` 的 multi-device / phase checkpoint 规则。每个 Phase 必须依次完成：实现 → 阶段测试与检查 → 更新本文件实际状态 → 独立 commit → 正常 push；push 成功后方可进入下一 Phase。

正式 Release 与测试构建必须绝对隔离。本任务可以实际运行 Internal Test Build，但不得创建 Stable Tag / Release；如需创建 Prerelease，必须先取得用户确认。

## Phase 1 — Version model + packaging Spike

状态：COMPLETE

目标：

- 在 `Directory.Build.props` 建立唯一产品版本来源，并让 Assembly / Informational Version 与 About 展示由此派生。
- 建立 `scripts/version.ps1` 及其测试，支持 `print`、`check`、`set`、`assert-not-lower`。
- 固定并验证 Velopack CLI，建立统一 `scripts/build-windows-package.ps1`。
- 验证 `.NET 10 + Avalonia + win-x64 self-contained` 能生成 Setup、Portable、更新 metadata 和 SHA-256。
- 保证安装包目录与 `%LocalAppData%\FinalCheck` 用户数据目录分离，且 Core / Documents / Comparison / Data 不依赖 Velopack。

完成标准：版本脚本测试通过；Release build/test 通过；本地 Test package Spike 产物完整且 checksum 可复验；记录初始包体积。

实际完成（2026-09-12）：

- 统一版本源为 `Directory.Build.props`；未发布源码基线 `0.1.0-alpha.0`，About / 侧栏由 Informational Version 派生，不再硬编码。
- 固定 vpk / Desktop Velopack 1.2.0，生命周期在数据库和 UI 前执行；包 ID `FinalCheck.App` 与数据目录 `FinalCheck` 分离。
- `version.ps1` 四个命令及正/负向 SemVer、独立版本源检查测试通过。
- Windows Release build：0 warning / 0 error；xUnit 79/79 PASS。
- 本地 `0.1.0-dev.1` Setup / Portable / nupkg / test metadata / SHA256 全部生成并通过完整产物校验；publish 122.28 MiB、Setup 58.98 MiB、Portable 54.52 MiB。安装/卸载将在 Phase 4 验证。
- 设计依据见 ADR-0004，测量追加至 performance baseline。未创建 Tag / Release，核心四层无 Velopack 依赖。

## Phase 2 — Test Build Action

状态：COMPLETE

目标：新增 `.github/workflows/build-test.yml`，仅由 `workflow_dispatch` 触发，以只读权限生成唯一的 `0.1.0-dev.<run>.<attempt>` Windows 测试 Artifact，保留 14 天，不 commit、不 push、不创建 Tag / Release、不修改正式版本文档或稳定更新通道。

完成标准：工作流静态校验通过；实际运行一次并确认 Setup、Portable、SHA256 与 updater metadata 可下载；确认 main、Tag、Release 均未被测试工作流修改。

实际完成：`build-test.yml` 已实现只读权限、runner ephemeral dev 版本、Release build/test、统一打包与 14 天 Artifact；现有 CI 加入版本脚本验证。需要先推送此安全检查点以注册工作流，再实际 dispatch、下载并校验产物，记录远程 main / Tag / Release 不变后完成 Phase。

首次远程 Run `34682956763` 的版本测试断言通过，但预期失败子进程留下 exit code 1，导致 Actions wrapper 错判；未打包/发布。已明确成功退出码并增加同包装器本地复验，待修正后的实际 Run 验收。对应 CI 的 macOS job PASS。

最终验收（2026-09-12）：

- 安全检查点 `e8a740b` 与退出码修正 `7c33cbd` 均正常推送。
- [Internal Test Run 34683055427](https://github.com/bluntvoice/Final-Check/actions/runs/34683055427)：PASS，版本 `0.1.0-dev.2.1`。
- Artifact `final-check-v0.1.0-dev.2.1-windows-test`（ID `10294667042`）已实际下载并解压；ZIP digest 与 GitHub 提供的 SHA256 一致，产物内全文件 checksum、metadata、Portable/About assembly 版本均 PASS。14 天保留至 2026-09-26。
- Setup 61,842,439 B；Portable 57,168,001 B；publish 128,217,746 B。
- 测试 workflow 前后远程 main 均为 `7c33cbd36326da8c537ca2f950f63189e3b36a71`；Tag 0、Release 0，未发生远程写入。
- [CI 34683050582](https://github.com/bluntvoice/Final-Check/actions/runs/34683050582)：Windows build/test 与 macOS core compatibility 均 PASS。actionlint 1.7.12 校验 PASS。

## Phase 3 — Release Action

状态：COMPLETE

目标：新增 `.github/workflows/release.yml`，支持 Prerelease / Stable，提供显式真实发布确认、24 小时保护、版本和 Tag 防护、默认分支变化保护、修改文件 allowlist、完整 build/test、原子 commit + annotated Tag push 及 GitHub Release 产物上传。

完成标准：YAML 和脚本静态检查通过；使用本地 dry-run / 负向用例验证 fail-closed 保护；不创建真实 Tag 或 Release。

实际完成（2026-09-12）：

- `release.yml` 已实现显式确认、默认分支、SemVer/channel/版本不倒退、本地和远端已有 Tag、24h、构建期间 base SHA、allowlist、完整 Release build/test、bot commit + annotated Tag 原子 push、Release notes 与全产物 GitHub Release 上传。Prerelease 不改稳定 README且非 latest，Stable 更新 README并 latest。
- `ReleaseSafety.psm1` / `prepare-release.ps1` / `update-release-docs.ps1` 提供统一可测试门禁；建立 CHANGELOG 与稳定 README marker，详细发布流程文档已建立。
- actionlint 1.7.12、全部 PowerShell AST、版本脚本与 Release infrastructure 测试 PASS。
- 隔离的临时 client/peer/bare Git fixtures 验证了未确认、错误 channel/版本、版本倒退、空 notes/summary、已有本地/远端 Tag、24h 默认拒绝和主动 override、中文多行 notes、历史 CHANGELOG保留、Prerelease README不变、Stable README同步、重复日志拒绝、意外代码修改拒绝、分支并发变化拒绝，以及已有远端 Tag 导致原子 push 两个 ref 均不变。测试夹具从未使用 Final Check origin，结束后移除。
- Windows Release build：0 warning / 0 error；xUnit 79/79 PASS；Assembly Informational/File Version检查 PASS。
- 未触发正式 Release 工作流，Final Check 仓库未创建真实 Tag / Release。GitHub Release API 与原子 Git push 非同一事务的恢复限制已明确记录。

## Phase 4 — Packaging verification + docs

状态：NOT STARTED

目标：完成测试安装包的本机可行验证（环境安全允许时包含安装、启动、版本、数据目录和卸载），补充 `docs/development/release-process.md`、性能基线、开发索引与 `AGENTS.md` 简要发布入口。

完成标准：Windows Release build/test 通过；macOS compatibility CI 通过；文档与实际脚本一致；记录 Setup、Portable、publish 及安装后体积和已知限制。

实际完成：待完成。

## 最终验收

- Existing CI：待验证
- Internal build workflow：待验证
- Test installer / Portable / SHA256：待验证
- Test workflow `contents: read` 且无 Tag / Release / main 写入：待验证
- Stable / Prerelease workflow syntax：待验证
- Version scripts：待验证
- Windows build/test：待验证
- macOS core CI：待验证
- Working tree：待验证
