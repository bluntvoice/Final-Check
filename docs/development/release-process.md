# Final Check release process

## 工作流与边界

| 类型 | 触发 | 版本例子 | 输出 | 仓库权限 |
|---|---|---|---|---|
| CI | push main / pull request | 源码版本 | Windows build/test、macOS core compatibility | contents: read |
| Internal Test Build | 手动 `Build Windows test installer` | `0.1.0-dev.2.1` | Actions Artifact，14 天 | contents: read |
| Prerelease | 手动 `Release Windows installer` + 显式确认 | `0.1.0-beta.1` | annotated Tag + GitHub Prerelease | contents: write |
| Stable | 同上 | `0.1.0` | annotated Tag + latest GitHub Release | contents: write |

测试安装包不是 Release，绝不 commit、push、创建 Tag/Release、更新 README/CHANGELOG 或发布稳定 metadata。正式 Release 仅通过 `workflow_dispatch`；推 Tag 本身不会绕过发布确认自动发版。

## Version

唯一版本源：根目录 `Directory.Build.props` 的 `Version`。未发布源码基线是 `0.1.0-alpha.0`，产品版本线从 `0.1.0` 开始；alpha 在 SemVer 中先于 beta 和内部 dev 构建。严禁在 `.csproj` 中维护另一份产品/Assembly/Informational version。

Assembly/File Version 由 SDK 派生为数字核心 `x.y.z.0`；Informational Version 保留完整 SemVer。About / 侧栏读取 App assembly metadata。打包校验验证 Informational/File Version、Portable `sq.version` 与 Velopack release metadata 一致。Installer/Portable 的文件名带完整版本；Setup bootstrap 的自身工具版本不能替代内嵌包版本。

```powershell
./scripts/version.ps1 print
./scripts/version.ps1 check
./scripts/version.ps1 assert-not-lower 0.1.0-beta.1
./scripts/version.ps1 set 0.1.0-beta.1
./scripts/test-version.ps1
./scripts/test-release-infrastructure.ps1
```

`set` 仅写唯一版本源，不代表已发布。正式版本必须通过 Release 工作流同步 CHANGELOG、Stable README、构建、Tag 和 GitHub Release。SemVer 使用数字核心与逐段预发布比较，不使用简单字符串大小比较。

## Internal Test Build

在 GitHub Actions 选择 **Build Windows test installer → Run workflow**，从最新默认分支运行，`target_version` 输入不带 v 的 `x.y.z`（默认 `0.1.0`）。工作流在 runner 工作副本临时设为 `target_version-dev.<run_number>.<run_attempt>`，不写回 main。

Restore tools → validate/version tests → restore/build/test Release → win-x64 self-contained publish → Velopack pack → 版本/metadata/SHA256 校验 → upload。成功后在 Run 页 **Artifacts** 下载 `final-check-v<version>-windows-test`。缺少文件会失败，不上传空 Artifact。重跑 Run 的 attempt 不同，版本也不同。

## Prerelease / Stable

发布前先确认是交付真实用户而非仅下载测试产物，复核实际变更、许可、版本亮点、最近 Tag 时间以及是否能合并到后续发布。测试请使用 Internal Test Build。本任务建设期间没有创建任何真实 Tag/Release。

正式发布选择 **Release Windows installer → Run workflow**：

1. 只能从 repository default branch 的最新代码运行。
2. 输入 `version`（不带 v）；Stable 必须 `x.y.z`，Prerelease 必须带后缀，例如 `x.y.z-beta.1`，不得使用内部 `-dev.*`。
3. 选择 channel，并填写保留换行/Markdown/中文的 `release_notes`。Stable 另填 `readme_summary`；Prerelease 不更新稳定 README。
4. 只有确为真实用户发布时才勾选 `confirm_real_release`；未勾选直接拒绝并提示使用 Test Build。
5. 默认距最近发布 Tag 不足 24 小时拒绝；只有明确必要且用户主动勾选 `allow_release_within_24h` 才可继续。
6. 校验唯一版本源、目标不倒退、工作区干净、本地/远端 Tag 均不存在、默认分支 SHA 等于构建 base SHA。
7. 临时写目标版本、追加 CHANGELOG `vX.Y.Z` 条目，Stable 更新 README 当前正式版本/摘要；执行脚本测试及完整 restore/build/test Release。
8. 统一脚本 publish/package，复验产物与 SHA256。自动修改仅允许 `Directory.Build.props`、`CHANGELOG.md` 和 Stable 的 `README.md`；未知文件、删除或缺少预期变更均拒绝。
9. commit/tag 前重新 fetch 默认分支；若 base SHA 已变，立即失败，从最新代码重跑，不覆盖新 main。
10. bot 创建 `chore(release): vX.Y.Z` commit 和 annotated `vX.Y.Z` Tag，通过 `git push --atomic` 一次正常推送两个 ref。不 force、不删除/重写旧 Tag。
11. 创建 `Final Check vX.Y.Z` GitHub Release，使用 UTF-8 Markdown notes 和所有必要产物；Prerelease 标记 prerelease 且非 latest，Stable 标记 latest。

同版本通过 `release-{version}` concurrency 串行，`cancel-in-progress: false`。任意版本、测试、打包、checksum、Tag、分支或 allowlist 失败都 fail closed。

**失败恢复边界：** Git branch+Tag push 是原子的，但 GitHub Release API 与 Git push 不是同一个事务。如果原子 push 已成功而 Release 创建/上传失败，必须保留 commit/Tag，调查后从该唯一 Tag 及对应校验通过的产物恢复 Release；不得删 Tag、重写历史、强推或直接重跑被已有 Tag 门禁拒绝的发版流程。重新构建会产生新的二进制 hash，不能假称为原始已验证资产。分支保护、组织权限和 GitHub 服务错误也必须如实处理。

## Packaging / Artifacts

Windows 使用 .NET 10 + Avalonia、Release、win-x64 self-contained，不要求用户另装 .NET Runtime，未启用 trimming / NativeAOT。Velopack 1.2.0 CLI 固定于本地工具 manifest；runtime 依赖仅在 Desktop，生命周期入口先于数据库/UI。

统一脚本 `scripts/build-windows-package.ps1` 接收 `PackageKind`（Test/Prerelease/Stable）、完整 `Version`、空 `OutputDirectory`，可选 `ReleaseNotesPath`。正式包版本必须等于源码版本；Test 只校验基础版本不倒退，不把临时 dev 版本当正式发布序列。

| 文件 | 用途 |
|---|---|
| `FinalCheck-v<version>-win-x64-Setup.exe` | 普通用户的 per-user 安装程序 |
| `FinalCheck-v<version>-win-x64-Portable.zip` | 完整 Portable layout，解压后运行根目录启动 stub |
| `FinalCheck-v<version>-win-x64-SHA256.txt` | 每个输出文件的 SHA-256 完整性校验 |
| `FinalCheck.App-<version>[-channel]-full.nupkg` | Velopack 完整 updater package，名称/内容不改 |
| `releases.<channel>.json` | 原样保留的版本、package hash/大小与多行 notes feed |
| `assets.<channel>.json` | 上传清单；Setup/Portable 引用同步到友好文件名 |
| `*-package-metrics.json` | publish/runtime payload、Setup/Portable 实测大小；不代表安装后总大小 |

所有 metadata 与 updater 包一并上传，不能只上传 Setup。可运行 `verify-windows-package.ps1 -Directory <目录> -Version <版本> -PackageKind <类型>` 复验。Artifact ZIP digest 是 Actions 层校验；SHA256.txt 是解压后的逐文件校验。SHA-256 保证完整性，不能替代发行者签名。

## Data safety / 安装

固定包 ID `FinalCheck.App`，默认安装到 `%LocalAppData%\FinalCheck.App`；用户数据库保持 `%LocalAppData%\FinalCheck\finalcheck.db`。数据、Snapshot、设置、日志、Backup 均不得保存到安装目录或 `current`。不要使用 `Setup --installto` 指向数据目录。Portable 也使用同一用户数据目录，**不是携带数据库的隔离沙盒**；安装/Portable 两种运行方式不能同时打开同一数据库执行开发实验。

Velopack 更新替换 `current`，卸载移除整个包 ID 目录。因此包 ID 不能改成 `FinalCheck`。Install/uninstall hooks 在数据库初始化前快速退出；源码运行和普通启动才访问用户数据库。手工验收已有用户数据时应先备份、记录 schema/hash，并避免运行会升级现有 schema 的旧/实验构建。

安装包当前未签名，Windows 可能显示 SmartScreen/发行者提示；仅信任来源明确且 checksum 通过的产物，不关闭系统安全保护。可使用官方 `Setup --silent` 禁止安装后自动启动，再从安装目录显式启动验证。

## Updater readiness / 平台范围

- Stable metadata channel 为 `win`；Prerelease 为 `beta`；Internal Test 为 `test` 且只在 Actions Artifact。
- 未来 `IUpdateService` 应默认查 Stable，beta 需明确选择；本地 dev/beta 构建也必须能按 SemVer 识别远程 Stable。不能把本地安装 channel 盲目视为唯一远程 feed。
- 未来保持多行 Release notes、下载进度、checksum、失败重试和安装失败不影响旧版继续运行；本轮 `DeferredUpdateService` 没有实现这些功能，不宣称 updater 已启用。
- Windows x64 是唯一安装包平台；macOS 继续 Core/Documents/Comparison/Data compatibility CI，不新增官方 macOS 包。

体积预算与真实测试证据见 [Performance baseline](performance-baseline.md)；执行状态见 [Windows packaging task](../tasks/windows-packaging-and-release-v0.md)。代码签名、Updater UI、实际正式发布的端到端验收仍是后续事项。
