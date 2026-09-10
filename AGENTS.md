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

本规则只定义后续实施基准，不代表当前仓库已经配置 GitHub Actions、Release 或软件更新机制。

## In Line 项目经验复用

数据备份恢复、归档、回收站等设计优先参考 In Line 项目已经验证的管理方式和实际经验。

特别避免：

- 导入失败原因不明确
- 导入成功后页面不显示
- 恢复后必须重启
- 恢复需要多个不必要步骤
- 静默覆盖现有数据
