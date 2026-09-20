# Final Check 验证策略（Verification Infrastructure Spike）

本文件记录独立验证基础设施 Spike 的**已实测能力**与后续建议。它不改变当前产品需求、安装器或发布门禁；正式验收不能把 Debug 隔离窗口冒充已安装的 Test Build。任务规范见 [verification-infrastructure-spike.md](../tasks/verification-infrastructure-spike.md)。

## 验证分层与统一入口

| 层 | 目标 | 当前状态 | 推荐时机 |
|---|---|---|---|
| L1 单元/集成 | 领域规则、Open XML、SQLite、服务和 ViewModel | 现有五个 xUnit 工程 | 每次 PR/push |
| L2 Avalonia Headless | 真实 XAML 树、绑定、布局和无系统对话框的交互 | 原型已运行，加入 solution | 每次 PR/push |
| L3 Verification Harness | 多模块贯通、结构化结论、原件安全、隔离 DataRoot | 原型已运行，加入 solution | 每次 PR/push |
| L4 Windows UI Automation | 真正桌面进程的导航与状态读取 | FlaUI.UIA3 Debug 隔离进程原型已运行；安装包未测 | 专用交互式 Windows runner / 手动触发 |
| L5 人工/Computer Use | 视觉与体感、系统级拖拽、安装器可理解性 | 仍须保留 | 阶段最终验收 |

在仓库根目录运行 `./scripts/verify.ps1`：依次 restore、Release build、整个 solution 的 Release test（含 Headless）、Verification Harness JSON 检查。任一步失败返回非零；最后输出各检查与 Overall。Windows UIA 不在默认入口中，因为它要求可交互、未锁定的桌面。单独运行：

```powershell
dotnet test tests/FinalCheck.Ui.Headless.Tests -c Release
dotnet run --project tools/FinalCheck.Verification -c Release -- verify --format json
dotnet run --project tools/FinalCheck.WindowsUi.Smoke -c Release -- `
  --app <隔离工作树的 Debug FinalCheck.Desktop.exe>
```

`Verification` 是开发工具，不是给最终用户的正式 CLI。它在 `%TEMP%\FinalCheckVerification\<GUID>` 下创建有 owner marker 的隔离 DataRoot 和原件夹具；成功后只删除本次拥有的目录，失败时保留并报告路径。不会重定向或打开用户的正常数据库。退出码 0=全部通过、1=检查失败、2=参数错误。JSON 包含 success、runId、dataRoot、retained、durationMs、各 check 的 name/status/stage/reason/durationMs/metric。当前真正执行 SQLite schema/integrity/foreign-key、DOCX 解析、Comparison 预期差异、Snapshot/Comparison 重开、Project/明确角色版本重开、原始 DOCX SHA-256 和未复制到 DataRoot 的检查。生成式、非敏感 DOCX 放在 DataRoot 外。

## Avalonia Headless 的已验证边界

基于 Avalonia/Headless 12.1.2 与现有 xUnit 2 工程，采用 `HeadlessUnitTestSession` 手动初始化（官方 Avalonia 12 的 xUnit adapter 对应 xUnit 3，未为了 Spike 迁移全部测试）。在真实 `MainWindow`、XAML、DataContext、layout/control tree 中验证了 AutomationId、Name、类型查找、按钮/命令、键盘 Space、TextBox 输入与绑定、ListBox、Comparison 结果页的搜索/Tab/状态按钮。相关测试纳入 solution 正常 build/test，不启动实际 OS 窗口。参考 [Avalonia Headless 官方文档](https://docs.avaloniaui.net/docs/testing/setting-up-the-headless-platform) 与 [Avalonia 12 迁移说明](https://docs.avaloniaui.net/docs/avalonia12-breaking-changes)。

当前 Headless 结果页实验发现：直接将 `ComparisonChangeList.SelectedIndex` 设为第二项时，外层 `SelectedEntry` 更新，但 `SelectedChange` 在嵌套列表刷新后成为 null。原型为了继续验证状态命令，明确选择了目标 ViewModel change；这**不是**完整列表选择用户路径通过的证据。应在后续业务阶段作为独立缺陷定位，不在本 Spike 改写产品逻辑。Headless 也不证明 OS File Picker、Windows UIA tree、真实滚轮/触控板、系统级 Drag & Drop、安装器和视觉质量。推荐作为正式测试层，而非替代最终桌面测试。

## Windows E2E 方案取舍

本次只对 **FlaUI.UIA3 5.0.0** 做了本机实测；FlaUI.Cli/Appium/WinAppDriver 未安装，也未虚构运行时间。FlaUI 的 [项目说明](https://github.com/FlaUI/FlaUI) 与 [NuGet 包](https://www.nuget.org/packages/FlaUI.UIA3)；可选命令行实验工具见 [FlaUI.Cli](https://github.com/kodroi/FlaUI.Cli)。Appium [Windows Driver](https://github.com/appium/appium-windows-driver) 依赖额外的 WinAppDriver 服务；其 [Windows Driver 文档](https://github.com/appium/appium-windows-driver#requirements) 和 [WinAppDriver Releases](https://github.com/microsoft/WinAppDriver/releases) 应在重新评估时核对版本与维护状态。

| 维度 | FlaUI 库 / FlaUI.Cli | Appium + WinAppDriver |
|---|---|---|
| Avalonia 兼容 | FlaUI.UIA3 已在真实 Final Check 主窗口找到 AutomationId 并 Invoke；CLI 未测 | 理论上依赖同一 Windows UIA tree，Final Check 未测 |
| CLI / Codex | 项目自有小型 .NET CLI 可精确控制与 JSON 输出；现成 FlaUI.Cli 非必需 | WebDriver 客户端易编排，但先要管理 Appium 与 WinAppDriver 服务 |
| 结构化结果 | 本原型 JSON/exit code 已验证 | 可由自建 client 包装；未实测 |
| 安装与维护 | 仅 NuGet FlaUI.UIA3 及专用 Windows 工程 | 额外 Node/Appium、driver、WinAppDriver 二进制与服务配置 |
| 速度/稳定性 | 本机单次 Debug 桌面烟测约 6 秒；需要多次 CI 样本才能评估稳定性 | 本机未运行，不能比较实测速度/稳定性 |
| CI | 需交互式、未锁屏 Windows session；不放普通无桌面 PR runner | 同样需要可交互 Windows 环境，另需服务治理 |
| Screenshot | FlaUI 可访问窗口/底层截图 API，本原型未验 | WebDriver 支持截图，未实测 Final Check |
| Drag & Drop | 低层输入可探索，但真实 Explorer→App 拖拽未验；最终保留人工 | 动作 API 不等于系统级可靠拖拽；未验 |
| Installer | 若 Wizard 暴露稳定 UIA 控件，FlaUI 有操作基础；Wizard 尚不存在，绝不能宣称通过 | 也可能操作可访问控件；未验，服务复杂度更高 |

**决定：Windows E2E 以项目自有 FlaUI.UIA3 小工具为基线**，只为关键流程增加稳定 AutomationId，不按中文可见文案定位。现成 FlaUI.Cli 可作为人工诊断/探树工具再评估，不成为 CI 关键依赖；暂不采用 Appium/WinAppDriver。理由是本机已验证端到端树与 Invoke、与 .NET 工程同栈、可直接输出机器可读结果、部署面较小。此决定并不表示安装器或已安装 App 已验证。

原型 `FinalCheck.WindowsUi.Smoke` 只接受**绝对 Debug Desktop.exe 路径**，使用 Debug-only `--developer-data-directory` 加 GUID 临时位置，启动实际 GUI，按 `MainQuickCompare` 找到导航按钮并调用 Invoke，再找到 `ComparisonStart`、验证未选择文件时 disabled，最后关闭并清理。拒绝 Release exe，以免无隔离参数时接触用户真实 DataRoot。该原型没有安装 Test Build；下一步应在隔离 Windows 用户/VM 验证安装产物、升级、卸载，不能把本次结果冒充它。

AutomationId 命名采用稳定 PascalCase 语义名，作用域为页面/视图，不依赖动态文案和列表索引；重复模板项需要父子作用域或持久身份。测试定位优先 AutomationId，Name 仅诊断备用。本 Spike 仅增加 `MainQuickCompare`、`MainPageTitle`、`ComparisonStart`、`TemplateList`、`TemplateNameInput`、`ComparisonSearch`、`ComparisonChangeList`、`ComparisonReview`，不改变视觉/命令/业务流程。

## 已安装 App 与 Installer 的后续验收

建议独立的、可交互 Windows VM/测试用户：PowerShell 验证 Test Build Artifact SHA/版本、安装进程、目录、快捷方式/注册表和卸载残留；FlaUI 对**已安装进程**的页面导航、选择/审阅/忽略/Undo、窗口关闭重启与状态读取；Verification Harness 或只读隔离数据库检查数据事实、文件 SHA 与 DataRoot。不要用 Release 主入口的正常用户配置做试验。完整流程的测试输入必须是生成式非敏感文档。

Stage B Installer Wizard 尚未实现，所以自定义安装目录控件、`D:\Applications\Final Check` 安装、原目录升级、卸载以及 DataRoot 留存全部**未验证**。FlaUI 技术上可以定位并操作暴露到 UIA tree 的 Path TextBox/Browse/Next，最终是否可行取决于 Wizard 的可访问性实现，应在 Wizard 首个原型时先跑这条最小 E2E。目录/文件存在与升级/卸载数据安全应由 PowerShell + Harness 验证，而不是仅从界面判断。安装路径解释是否容易理解至少人工检查一次。

原生 File Picker 不纳入大多数自动测试：服务/VM/CLI 直接传受控路径；只在一次系统 E2E 检验真实对话框。Drop handler、路径验证、导入流程可用 unit/headless/集成测试，仍保留一次真实 Explorer→Final Check 拖拽。Windows UIA 原型**未测试**系统拖拽。

## CI 分层与人工验收

- PR/main push：Windows `dotnet restore`、Release build、整个 solution 的 tests（含 Headless）、Verification Harness；macOS 继续只构建/测试 Core/Documents/Comparison/Data 兼容层。当前 workflow 尚未改动；脚本可以先在本地或后续 PR 中接入。UIA 的 Windows-only 工程在 macOS core job 不运行。
- Manual/nightly：专用、可交互 Windows runner 执行已安装 Test Build 的 FlaUI 烟测、打包结构/签名/哈希检查；runner 必须隔离、可销毁，失败保留日志、窗口树与截图。未建此 runner 前不能把原型称为 CI 验证。
- Release gate：上述自动化全绿，再做人工 UX 清单和安装/更新/卸载路径安全核对；Tag/Release 仍只在用户授权的正式发布流程中创建。

人工/Computer Use 保留：窗口大小变化与截断、视觉层级、慢/快鼠标滚轮和触控板手感、Leader/Follower 自然度、一次真实系统级拖拽、File Picker、Installer Wizard 易理解性及异常恢复体验。预估未来验收工作可由 CLI/现有 unit/integration 约 45–60%、Headless 约 15–25%、Windows UIA 约 10–20% 覆盖，留下约 10–20% 人工体验；这是**方案估计**而非已测覆盖率或硬 KPI，需随着 Stage A/B 验收项清单实际统计。

## Spike 的工程结论

采用 Headless 与 Verification Harness 作为正式新增测试层；Windows E2E 首选 FlaUI.UIA3 项目自有 CLI。下一阶段可立即使用 `verify.ps1`、Headless 和 Harness 检查逻辑与无系统对话框 UI，但安装产物、真实拖拽与体感仍按上述边界人工验收。未来 CI 接线和 installed-app/Installer 实测是独立后续工作，不由本 Spike 的本机成功结果代替。
