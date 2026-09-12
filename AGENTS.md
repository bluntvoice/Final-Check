# Final Check Agent 开发约束

## 项目说明

- 项目名称：Final Check
- 项目类型：本地桌面工具
- 核心方向：合同版本管理、合同比对、修订识别、修改整理、格式恢复

本文件是 Codex 及其他代码 Agent 执行仓库任务时必须优先阅读的仓库级规则入口，适用于整个仓库。详细产品需求不在此重复，以 `docs/PRD/` 中的版本化 PRD 为准。

## 当前版本与版本规范

Final Check 初始版本从 `v0.1.0` 开始。

后续涉及软件版本、PRD、CHANGELOG、Git Tag、GitHub Release、GitHub Actions 和软件内更新时，均使用语义化版本号。不得重新使用 `v0.1` 作为正式版本号。

## v0.1.0 技术基线

- .NET 10 LTS / C#
- Avalonia UI / MVVM
- Open XML SDK
- SQLite / Entity Framework Core 10
- xUnit

技术选型依据见 `docs/architecture/ADR-0001-technology-stack.md`。未经用户明确要求，不得替换上述技术栈或引入 Preview、RC、Experimental、nightly 依赖。

## 跨平台与轻量化约束

官方开发、主要测试和正式 Release 以 Windows 为主，但架构应保留 macOS/Linux 源码构建可能性。

`FinalCheck.Core`、`FinalCheck.Documents`、`FinalCheck.Comparison`、`FinalCheck.Data` 不得依赖 Windows-only API，包括 Word COM、Office Interop、Word.exe、Registry、explorer.exe、Windows Shell、WinUI 和 Windows App SDK。平台能力必须通过接口抽象，并在 Infrastructure/Desktop 中实现。

Final Check 必须保持轻量。新增大型依赖、native binary、浏览器 Runtime 或重型 SDK 前，必须评估必要性、包体积、空闲内存和跨平台影响。不得无必要引入 Electron、完整 Chromium Runtime、内置 AI 模型或大型机器学习 Runtime。

## 产品需求权威来源

当前产品需求以 [`docs/PRD/PRD-v0.1.0.md`](docs/PRD/PRD-v0.1.0.md) 为主要事实来源。

进行新功能、产品行为、页面流程、数据规则、文件管理、比对逻辑、格式恢复或版本管理相关工作前，必须先阅读相关 PRD。

后续出现新版本 PRD 时，应新建对应版本文件并保留历史版本，不得直接覆盖旧版 PRD。

## 需求冲突处理

出现以下情况时，不得自行选择最方便开发的方案：

- 用户新指令与 PRD 不一致
- 实现方案需要改变既有产品行为
- PRD 内部存在冲突
- 重大产品逻辑无法确定

应先向用户说明冲突、影响与已知事实，并优先提供清晰选项供用户确认。普通技术实现细节、明显 Bug 修复及不影响产品行为的小型调整，无需反复询问。

## 开发前阅读原则

每次任务执行前：

1. 阅读相关代码；
2. 阅读相关 PRD；
3. 阅读 `docs/architecture/` 和 `docs/development/` 中与任务相关的文档；
4. 理解现有实现及约束；
5. 再开始修改。

禁止为了快速完成任务而绕过现有设计。

## Task state recovery

For long-running or multi-stage work, the repository is the source of truth for implementation state.

If task state becomes unclear because of context compaction, session interruption, or other causes, do not rely solely on conversational memory or summaries to infer progress.

Before continuing:

1. Re-read `AGENTS.md`.
2. Re-read the active task file under `docs/tasks/`.
3. Inspect `git status`, current diff, and recent commits.
4. Inspect the relevant implementation and tests.
5. Run the appropriate checks when necessary.
6. Reconstruct the actual completed and remaining work from repository state.

Do not repeat already completed work, silently skip incomplete requirements, or assume a task is complete because it was previously discussed.

## Multi-device development and phase checkpoints

Final Check is developed across multiple computers. The remote Git repository is the authoritative synchronization point for completed development progress; conversational memory and an unpushed local working tree are not substitutes for a remote checkpoint.

For every multi-phase task under `docs/tasks/`, complete this sequence for each Phase before starting the next one:

1. Complete the Phase as a coherent unit of work.
2. Run the tests and checks required by that Phase and confirm relevant existing behavior has not regressed.
3. Update the active task file with the Phase status and an accurate description of what was completed and verified.
4. Create a logically complete, understandable Phase commit whose message reflects the actual implementation.
5. Push the Phase commit normally to the remote repository and confirm the push succeeded.
6. Only then begin the next Phase.

Do not wait until an entire large task is complete before the first push. A Phase commit is a meaningful development milestone, not a reason to commit every small edit; keep minor related edits together until the Phase or a safe checkpoint is coherent.

If development must move to another computer before a Phase is complete:

- Prefer reaching a safe checkpoint that builds and has the relevant tests passing.
- Mark the Phase `IN PROGRESS` in the active task file.
- Record the completed work, remaining work, current test results, and known issues.
- If the checkpoint is coherent and will not break the normal branch, commit it with a clear checkpoint message and push it normally.
- If the work is explicitly incomplete or would leave the normal branch broken, create and push a temporary work branch instead of pushing broken code to the main branch.

Before continuing on another computer:

1. Fetch and safely pull the latest remote state.
2. Re-read `AGENTS.md`.
3. Re-read the active task file.
4. Inspect recent commits.
5. Inspect `git status`.
6. Continue from the repository's actual state rather than conversational memory.

Multi-device synchronization does not permit destructive Git shortcuts. The prohibitions in `Git 操作安全` continue to apply, especially force push, `reset --hard`, history rewriting, and other operations that could discard or overwrite work.

## 修改范围原则

禁止：

- 无关的大规模重构
- 擅自替换技术栈
- 擅自删除已有功能
- 为解决局部问题修改大量无关模块
- 通过临时 Hack 掩盖根本问题
- 在未理解现有实现前直接重写

## 文档同步

如果代码改动导致产品行为、数据模型、重要架构、发布流程、软件更新机制或开发规范发生变化，必须同步检查并按需更新：

- `docs/PRD/`
- `docs/architecture/`
- `docs/development/`

不得让代码已经变化而文档仍描述旧逻辑。

## 测试要求

每次完成代码任务后，应根据项目实际情况执行相关的类型检查、lint、单元测试、构建检查和必要功能测试。

不得在没有任何验证的情况下直接宣称任务完成。无法运行某项检查时，必须明确说明未执行的项目及原因。

## 数据安全原则

Final Check 涉及合同、历史版本和结构化文档数据，后续开发必须遵循：

- 不主动修改用户原始 DOCX
- 不静默覆盖历史比对结果
- 不因重新比对删除旧结果
- 数据升级必须考虑兼容
- 删除操作应避免不可逆误删
- 数据恢复优先采用非破坏式方案
- 文件路径、哈希、快照与历史记录必须保持一致
- 任何迁移逻辑均须考虑失败恢复

## 安装与数据路径长期原则

- 安装位置与应用数据位置是独立概念；业务数据永不写入可替换/卸载的安装目录。
- Windows 首装必须显示并允许浏览选择目录；Test / Prerelease / Stable 使用同一机制，更新/维护/卸载沿用实际实例位置，不提供多目录 side-by-side。
- 业务存储统一通过独立 DataRoot 与平台抽象定位；bootstrap 在默认配置位置保留最小启动信息，不以目标 SQLite 作为唯一定位源。
- 更改数据位置必须完整一致性复制、验证、可恢复切换，失败保护旧数据，成功默认保留旧数据；禁止用剪切/自动删除代替迁移，不未经验证宣称支持 SQLite 网络盘。
- 详细路径/兼容/迁移与安装器 Spike 分别见 `docs/architecture/storage-and-paths.md`、`docs/architecture/installer-architecture.md`。需求/方案落档不代表功能已实现；未验证前不替换现有 Velopack packaging workflow。

## Git 操作安全

执行 Git 操作前必须先检查 `git status`。

未经用户明确要求，禁止：

- `git push --force`
- `git reset --hard`
- 删除远程分支
- 重写 Git 历史
- 删除用户未提交内容
- 自动发布 Release
- 自动删除 Tag

## GitHub Actions、Release 与软件更新基准

Final Check 以及用户后续新的软件项目，其 GitHub Actions、Release 和软件内更新机制，默认以“群聊拾遗（WeChatDataAnalysis）”项目当前已经验证的实现标准作为基准。正式配置时优先复用成熟逻辑。

至少包括：

- Windows 自动构建
- 安装包生成
- Artifact
- Tag / Release
- 正式版 / 测试版
- 软件内检查更新
- 下载进度展示
- 测试版能够识别正式版更新
- Release 更新说明正确换行
- 下载失败可重试
- 安装失败可重试

当前工作流入口：

- CI：push main / pull request，Windows build/test 与 macOS core compatibility。
- Internal Test Build：手动构建，仅只读 Actions Artifact，不创建 Tag / Release，不写回正式版本。
- Prerelease：显式确认的预发布，annotated Tag 与 GitHub Prerelease。
- Stable：显式确认的稳定发布，annotated Tag、latest Release 与稳定 README。

长期详细发布规则以 [`docs/development/release-process.md`](docs/development/release-process.md) 为准；执行前须先阅读。不得把正式 Release 当作构建测试；本轮基础设施建设不代表已实际发布稳定版本，也不代表软件内 updater UI 已实现。

## In Line 项目经验复用

数据备份恢复、归档、回收站等设计优先参考 In Line 项目已经验证的管理方式和实际经验。

特别避免：

- 导入失败原因不明确
- 导入成功后页面不显示
- 恢复后必须重启
- 恢复需要多个不必要步骤
- 静默覆盖现有数据
