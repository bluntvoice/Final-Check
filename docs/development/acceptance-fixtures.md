# 跨设备合成验收夹具

`tools/FinalCheck.AcceptanceFixtures` 将此前本机临时 FixtureTool 的纯合成生成部分纳入仓库，不包含数据库审计、用户库备份或历史数据导出命令。仅依赖现有 Open XML / Documents 项目，无新 NuGet 包、Windows API、GUI、SQLite 或业务代码改动。加入 solution；生成与校验测试由 Documents.Tests 执行，现有 Windows/macOS CI 会正常覆盖。

## 在另一台电脑准备样本

安全同步后，在仓库根目录用 PowerShell 7 执行（需要仓库指定的稳定 .NET 10 SDK）：

```powershell
# 串行 restore/build；默认在 Git 忽略的 artifacts 下生成唯一新目录。
./scripts/acceptance-fixtures.ps1

# 样本转移后或怀疑变动时按需复查；Generate 已自动校验，不必立即重复 Verify。
./scripts/acceptance-fixtures.ps1 -Action Verify -OutputDirectory '<上一条输出的绝对目录>' -NoBuild

# 或指定一个尚不存在的绝对目录；不覆盖已有目录，即使已有目录为空。
./scripts/acceptance-fixtures.ps1 -OutputDirectory 'D:\FinalCheckSynthetic\acceptance-run-01'
```

也可以跨平台直接调用 CLI；`generate` / `verify` 输出单份 JSON（success、action、manifest），成功 exit 0、校验/IO 失败 exit 1、用法错误 exit 2。`dotnet run` 的构建信息不是 CLI JSON；采集自动报告时先 build 再运行 DLL。

```powershell
dotnet build tools/FinalCheck.AcceptanceFixtures -c Release -m:1 /p:UseSharedCompilation=false
dotnet tools/FinalCheck.AcceptanceFixtures/bin/Release/net10.0/FinalCheck.AcceptanceFixtures.dll generate '<新绝对目录>'
dotnet tools/FinalCheck.AcceptanceFixtures/bin/Release/net10.0/FinalCheck.AcceptanceFixtures.dll verify '<已有夹具绝对目录>'
dotnet test tests/FinalCheck.Documents.Tests -c Release --filter FullyQualifiedName~AcceptanceFixtureTests
```

输出是 18 份 DOCX 与 `manifest.json`；清单 schema 1 记录 SyntheticOnly、相对文件名、大小、SHA-256、正文段落数、表格数和批注数。校验固定目录清单、hash/大小/结构统计及 Open XML schema，拒绝清单路径穿越/重复名称/符号链接；不读取数据库、bootstrap 或原始用户文档。失败保留现场，不自动清理目录。生成内容和 XML 结构固定，但不同生成时间的 DOCX ZIP 元数据可能不同；不要把跨次 ZIP hash 相等当作验收要求，每次使用本次清单核验。

## 样本目录与人工检查

| 样本 | 合成场景 | 检查重点 |
| --- | --- | --- |
| small-baseline / small-current | 20 段、30→60 日、宋体→仿宋、24→22 半磅字号值、100→200 表格金额、合成批注 | Context 精确高亮、表格结构、批注、审阅/Undo、单项/全部格式忽略、Full 往返、普通/短窄/最大化窗口 |
| medium-baseline / medium-current | 400 段，同类文字/格式/表格/批注 | 连续上一/下一项、搜索/筛选、较长文档响应 |
| long-baseline / long-current | 正文 400/508 段，集中删除 20 段、新增 128 段、部分格式/文字变化、表格与批注 | Full Mode 慢/快滚轮、滚动条、换侧、关闭/重开联动；按逻辑锚点而非像素比例跟随 |
| 00-template-v1 到 05-template-v2 | 纯合成模板→我方→对方→我方新版本→对方新版本→模板更新，30/30/60/45/90/35 日 | 用户明确指定角色、版本编号、当前基准、独立历史，更新模板不覆盖旧比对 |
| ignore-baseline / ignore-current | 标点、自定义字符 ※、段首编号、短页码、金额/日期/期限 | 保留 raw 差异；短页码低可信时保守保留，不将其声称为可靠页码隐藏通过 |
| page-baseline / page-current | Page 100→Page 101，同时有 300→400 万元、30→60 日、2026→2027 年 | 可靠映射时检查页码规则；金额/日期/期限不得误隐藏；诊断/未匹配时如实记录，不降低匹配阈值 |
| move-baseline / move-current | 第 2 段移到第 4 段且 30→60 日，末段重写 | Move 原/现上下文、精确文字变化、重写低可信/Partial 诚实展示 |

新的 long 样本是可重复的等价长文场景，不承诺与历史 Debug 400/508 夹具有相同 ChangeItem 数量或 ID。这些检查是待执行步骤，生成/校验成功不是产品人工验收通过。

### Top 3 / 历史模板候选

在本机可丢弃的测试数据中，通过正式模板中心手动导入同一 `small-baseline.docx` 至两个启用的合成模板；第一个保留一个 current 版本，第二个导入两个版本并明确核对只有一个 current。保持原有模板/历史，不删除真实数据。选择 `small-current.docx`，观察实际推荐、分数、current/历史标记和 Top 3；若环境中已有其他模板干扰，记录现场或使用独立开发测试环境，不清空正常用户库。

推荐不得自动执行。显式选择历史版本并点击开始，核对工作台和项目历史的模板版本；再次比较应追加独立历史，而不是覆盖。评分受已导入候选及现有阈值影响，不硬编码必须得到历史机器上的 90% 或固定 RecordId。这一步仍需实际 UI 验收，生成器不导入模板、不创建项目/数据库。

### Installed acceptance 与 CLI 边界

按 [风险验证标准](README.md#按风险验证标准) 与 [Stage A task](../tasks/usability-workflow-correction-v0.md) 选择本次受影响路径；这些样本是可选工具箱，不是每次都运行全部场景。交付测试包时核验覆盖全部可执行改动的产物并保护既有数据，记录 version、SHA、Run ID 和实际结果；纯文档提交不重打包。真实滚轮、窄窗口、拖放和视觉体验不能由生成器、Open XML schema 或 Headless 冒充。

不再维护一次性的 Stage A FlaUI 全流程 runner；按需使用现有 Debug 隔离入口和真实输入完成冒烟。合成夹具与正常自动测试保留，不额外搭建通用 Verification Harness，不将开发窗口冒烟等同于 Setup 生命周期验收。

## 数据与 Git 边界

只提交生成器、脚本、测试和步骤文档；生成结果默认落在 `artifacts/`，不提交 `.docx`、manifest、测试数据库或原始机器日志。自定义输出目录的产物也不要 `git add`。跨设备重新运行即可获得同一合成场景，但不复刻原机的项目/模板 ID、review 状态或业务历史。应用 DataRoot、bootstrap、原始 DOCX 路径仍按现有存储规则管理，本工具不是备份恢复或多设备数据同步功能。
