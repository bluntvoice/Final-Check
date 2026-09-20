现在为 Final Check 执行一次独立的 **Verification Infrastructure Spike**。

本任务目标是：

> 评估并验证如何使用 CLI、Avalonia Headless、Windows UI Automation 等方式，替代目前大量依赖 Computer Use 的软件验收流程，使后续 Codex 能以更快、更稳定、可重复、可进入 CI 的方式完成自动验证。

本轮属于：

> 验证基础设施 / Spike

不是业务功能开发。

不要修改 Final Check 的产品逻辑，不要因为测试方便改变正式 UI 行为。

---

# 一、背景

当前 Final Check 在部分最终验收中需要 Codex 使用 Computer Use：

- 启动应用；
- 点击页面；
- 选择文件；
- 查看 Comparison；
- 切换筛选；
- 检查状态；
- 重启；
- 安装 / 卸载；
- 检查 UI 结果。

这种方式存在：

- 执行速度慢；
- 易受窗口位置、焦点、分辨率影响；
- 很难重复；
- 不适合 CI；
- 大量数据状态其实完全没有必要通过视觉判断；
- Codex 验收成本较高。

希望建立分层验证体系。

目标大致为：

```text
L1 CLI / Unit / Integration Tests
        ↓
L2 Avalonia Headless UI Tests
        ↓
L3 Final Check Verification Harness
        ↓
L4 Windows Installed-App UI Automation
        ↓
L5 Computer Use / 人工真实 UX 验收
```

Computer Use 不完全取消，但应尽量只保留：

- 视觉体验；
- 滚动手感；
- 系统级真实 Drag & Drop；
- 安装界面易用性；
- 少数无法可靠自动化的真实桌面场景。

---

# 二、执行前读取

先重新读取：

- `AGENTS.md`
- `docs/PRD/PRD-v0.1.0.md`
- 当前 development 文档
- 当前 testing / CI / release 文档
- Comparison UI architecture
- Project / Template / Version architecture
- Storage architecture
- Installer architecture
- 当前 test projects
- `.github/workflows/*.yml`

检查：

- 当前有哪些测试工程；
- Unit / integration / ViewModel 测试现状；
- Avalonia UI 是否已经有 Headless 基础；
- 当前 UI Automation / AutomationProperties 使用情况；
- Test Build 当前安装/验收流程；
- 当前哪些任务仍要求 Computer Use。

不要根据聊天记录假设仓库现状。

---

# 三、本轮先做 Spike，不全面迁移

本轮重点回答：

1. Avalonia Headless 是否适合 Final Check？
2. Windows UI Automation 哪种方案更适合？
3. 是否有必要建立 FinalCheck.Verification CLI / Harness？
4. 哪些现有 Computer Use 验收可以立即替代？
5. 哪些仍必须保留真实 GUI / Computer Use？

不要直接把全部现有验收重写。

---

# 四、方案 A — Avalonia Headless Spike

评估 Avalonia 官方 Headless Testing 能力。

建立最小测试原型。

建议新增测试工程，例如：

`tests/FinalCheck.Ui.Headless.Tests`

名称根据现有项目规范调整。

至少验证以下能力：

## A1. 启动应用级 View / ViewModel

无需打开真实桌面窗口即可：

- 创建主窗口或目标 View；
- 完成 DataContext；
- 执行 layout；
- 获取真实 Avalonia control tree。

---

## A2. 查找控件

验证能否稳定通过：

- Name；
- AutomationProperties.AutomationId；
- DataContext；
- 类型；

找到：

- Button；
- TextBox；
- ListBox / ItemsControl；
- Toggle；
- Tab；
- Dialog 内关键控件。

---

## A3. 输入模拟

至少测试：

- Button click；
- TextBox 输入；
- List item selection；
- keyboard navigation；
- command invocation。

---

## A4. Comparison Workspace 示例

如果当前 Stage A 的 Comparison Workspace 已存在，选择一个简单、不依赖 OS dialog 的流程验证：

例如：

```text
载入测试 Comparison
→ 选择某个 ChangeItem
→ 点击“已审阅”
→ 状态改变
→ 点击 Undo
→ 恢复
```

如果当前分支还没有该 UI，则使用现有稳定页面做等价测试。

不要因为 Spike 修改业务流程。

---

## A5. Headless 结论

必须回答：

- 是否稳定；
- 执行时间；
- 是否适合 CI；
- 哪些 Avalonia 行为无法在 Headless 覆盖；
- 是否推荐成为正式测试层。

---

# 五、方案 B — Windows UI Automation Spike

目标：

> 不依赖截图识别，直接通过 Windows Accessibility / UI Automation Tree 操作真正安装后的 Final Check。

优先评估以下方案：

1. FlaUI / FlaUI.Cli
2. Appium + WinAppDriver
3. 其他成熟 .NET Windows UI Automation 方案

不要一次全部重度集成。

先调研现有项目兼容性并选择 1～2 个最值得实际 Spike 的方案。

---

# 六、Windows UI Automation 最小验收

使用 Test Build 或当前可运行 Release build。

至少完成：

```text
启动 Final Check
→ 等待主窗口
→ 获取 Automation Tree
→ 找到一个主导航入口
→ 点击
→ 找到页面中的一个关键控件
→ 读取或修改状态
→ 截图（可选）
→ 正常关闭应用
```

整个过程：

> 不使用 Computer Use。

---

# 七、AutomationId 规范

检查当前 Avalonia 控件是否有稳定 Automation ID。

如果缺失：

允许在 Spike 中为少量关键控件增加：

`AutomationProperties.AutomationId`

但必须满足：

- 不改变视觉；
- 不改变业务行为；
- 不依赖动态中文文案定位；
- ID 稳定、语义明确。

例如：

```text
MainNavigation
QuickCompareButton
ProjectList
TemplateCenterButton
ComparisonChangeList
ReviewButton
IgnoreButton
UndoButton
BaselineSelector
```

不要一次给所有控件添加 ID。

Spike 仅添加完成验证所必需的最小集合。

并提出后续正式命名规范。

---

# 八、比较 FlaUI / Appium

最终报告必须做明确比较。

至少比较：

| 项目 | FlaUI / FlaUI.Cli | Appium / WinAppDriver |
|---|---|---|
| Avalonia 兼容 | | |
| CLI 友好 | | |
| Codex 易调用 | | |
| 返回结构化结果 | | |
| 安装复杂度 | | |
| 执行速度 | | |
| CI 可用性 | | |
| 稳定性 | | |
| Screenshot | | |
| 文件拖放支持 | | |
| Installer 测试 | | |
| 维护成本 | | |

不要只列功能。

最后必须给：

> Final Check 推荐采用哪一个作为 Windows E2E UI Automation 标准。

---

# 九、方案 C — FinalCheck Verification Harness

评估建立一个专用验证工具。

推荐方向：

```text
tools/
  FinalCheck.Verification/
```

或符合现有 Solution 规范的类似名称。

目标不是正式用户 CLI。

它是：

> 开发 / Codex / CI 专用验证入口。

---

# 十、Verification Harness 最小原型

建立一个最小可运行原型。

建议支持：

```powershell
dotnet run --project tools/FinalCheck.Verification -- verify
```

或者生成：

```powershell
FinalCheck.Verification.exe verify
```

至少执行 3～5 个真正有价值的验证。

例如：

## V1 Database

- 当前测试 DB 可以打开；
- schema 正确；
- integrity check PASS。

## V2 DOCX

使用 repository test fixture：

- parse；
- Snapshot；
- hash 不改变。

## V3 Comparison

两份 fixture：

- 运行 Comparison；
- 确认 expected change count / key difference。

## V4 Project

创建临时 DataRoot：

- 创建 Project；
- 添加 Versions；
- 创建 Comparison；
- reopen；
- 数据仍存在。

## V5 Original Safety

确认：

> 原测试 DOCX SHA-256 前后一致。

---

# 十一、结构化输出

Verification Harness 必须支持机器可读输出。

例如：

```powershell
FinalCheck.Verification verify --format json
```

返回类似：

```json
{
  "success": true,
  "checks": [
    {
      "name": "database",
      "status": "pass"
    },
    {
      "name": "comparison",
      "status": "pass",
      "changeCount": 12
    }
  ]
}
```

字段设计自行合理确定。

要求：

- exit code 0 = PASS；
- 非 0 = FAIL；
- 错误包含 stage / reason；
- 不要要求 Codex 从长日志里猜结论。

---

# 十二、隔离性

Verification Harness 默认必须使用：

> 临时 DataRoot

不要碰用户真实：

- SQLite；
- Project；
- Template；
- Comparison history；
- Working Copy。

建议：

```text
%TEMP%\FinalCheckVerification\<run-id>
```

执行结束：

正常清理。

失败时可以选择保留并打印位置用于排查。

不得使用真实用户数据做自动验证。

---

# 十三、未来验证入口设计

Spike 需要提出未来统一入口，例如：

```powershell
./scripts/verify.ps1
```

统一调用：

```text
dotnet restore
dotnet build
dotnet test
Headless tests
Verification Harness
```

返回最终摘要：

```text
Final Check Verification

Build                  PASS
Unit Tests             PASS
Headless UI            PASS
Database               PASS
DOCX Parse             PASS
Comparison             PASS
Project Persistence    PASS
Original File Safety   PASS

Overall                PASS
```

---

# 十四、Installed App 自动验收设计

Spike 不需要把完整 Installed Test 全做完，但必须设计正式方案。

未来应能够自动验证：

```text
安装 Test Build
→ 启动 Final Check
→ 进入页面
→ 操作关键控件
→ 关闭
→ 重启
→ 验证状态
→ 卸载
```

至少说明：

- 哪部分 PowerShell 完成；
- 哪部分 UI Automation 完成；
- 哪部分 Verification Harness 完成。

---

# 十五、Installer Wizard 自动验收预留

Stage B 后要验证：

```text
启动 Installer
→ 获取安装路径控件
→ 修改为 D:\Applications\Final Check
→ 安装
→ 检查安装目录
→ 启动
→ 更新
→ 保持 D盘
→ 卸载
→ DataRoot 仍存在
```

本 Spike 必须判断：

> 选定的 Windows UI Automation 方案是否能够操作 Installer Wizard。

如不能：

明确指出。

不要等 Installer 开发完才发现工具不适合。

---

# 十六、File Picker

原生 Windows File Picker 很可能不适合作为大多数自动验收的必要步骤。

设计测试时优先：

- ViewModel / service 直接传 test file；
- Verification Harness 传 path；
- UI Automation 重点验证应用行为。

只有最终系统级 E2E 才验证真实 File Picker。

不要让大量测试因为系统文件选择框变得脆弱。

---

# 十七、Drag & Drop

分别定义：

## 自动测试

验证：

- Drop handler；
- file validation；
- ViewModel / service。

## 最终真实验收

真实 Windows Explorer → Final Check Drag & Drop：

仍可以保留少量人工 / Computer Use 验收。

如果 Windows UI Automation 可以稳定执行系统级 Drag & Drop：

记录结果。

但不要为了“100% 自动化”构建高脆弱方案。

---

# 十八、Computer Use 保留范围

最终明确哪些测试仍应使用 Computer Use / 人工。

建议至少保留：

### UX / 视觉

- 页面是否拥挤；
- 信息层级；
- 按钮是否过大；
- 文字是否截断；
- 窗口 resize。

### 滚动手感

- 慢滚；
- 快滚；
- Trackpad；
- Leader/Follower 是否感觉自然。

### Drag/drop

至少一次真实系统拖拽。

### Installer

首次正式 Installer Wizard：

至少一次人工检查可理解性。

---

# 十九、目标比例

Spike 最终评估：

当前 Computer Use 验收项目中：

- 可被 CLI / Unit 替代多少；
- 可被 Headless 替代多少；
- 可被 UI Automation 替代多少；
- 仍需 Computer Use 多少。

目标方向：

> Computer Use 只承担最后约 10%～20% 的体验类验证。

这不是硬 KPI。

如果某类自动化明显比人工更脆弱：

不要为了数字强行自动化。

---

# 二十、CI 方案

提出后续 CI 分层建议。

例如：

## PR / Push

运行：

- build
- unit tests
- headless tests
- verification harness

## Windows Nightly / Manual

运行：

- installed-app UI Automation
- packaging smoke test

## Release Gate

运行：

- above all
- selected manual UX checklist

本 Spike 可只设计，不要求全部改 workflow。

---

# 二十一、性能

记录：

- Headless test startup；
- UI Automation startup；
- Harness 总时间。

目的之一就是：

> 比 Computer Use 快。

如果自动化一次需要几分钟且高度不稳定：

需要说明价值是否足够。

---

# 二十二、文档

新增建议：

`docs/development/verification-strategy.md`

内容至少包括：

1. Verification pyramid；
2. Unit / integration；
3. Avalonia Headless；
4. Verification Harness；
5. Windows UI Automation；
6. Computer Use；
7. Installed app；
8. Installer；
9. CI；
10. Manual UX checklist。

如最终选定某种 Windows UI Automation 技术：

记录理由和版本。

---

# 二十三、本轮不做

明确禁止扩大 Scope：

- 不重写 Comparison；
- 不修改项目业务逻辑；
- 不实现 Installer Wizard；
- 不实现 Stage A 新功能；
- 不增加 Three-way Compare；
- 不增加 PDF；
- 不新增正式产品 CLI；
- 不修改 Release 行为；
- 不发布新版本。

本轮只是验证：

> 如何更高效地验证 Final Check。

---

# 二十四、Tests / Build

本 Spike 自身不能破坏现有测试。

至少：

```powershell
dotnet restore
dotnet build -c Release
dotnet test -c Release
```

全部通过。

如增加 Headless / Verification 工程：

必须加入 Solution，并可单独运行。

---

# 二十五、Git

建议将 Spike 单独 commit。

如果需要多步提交，可以：

### Commit 1

`test: spike Avalonia headless verification`

### Commit 2

`test: spike Windows UI automation`

### Commit 3

`test: add Final Check verification harness`

### Commit 4

`docs: define Final Check verification strategy`

正常 push。

不得：

- force push；
- reset --hard；
- Tag；
- Release。

---

# 二十六、最终报告

严格按以下格式汇报：

## Verification Infrastructure Spike

### Current State

- Existing tests：
- Existing UI tests：
- Current Computer Use acceptance：

### Avalonia Headless

- Prototype：
- Controls：
- Input：
- Binding：
- Comparison example：
- Runtime：
- CI suitability：
- Limitations：
- Recommendation：

### Windows UI Automation

#### FlaUI / FlaUI.Cli
- Result：
- Automation tree：
- Launch：
- Click：
- Read state：
- Runtime：
- Limitations：

#### Appium / WinAppDriver
- Result：
- Setup：
- Compatibility：
- Runtime：
- Limitations：

### Recommendation

Final Check Windows E2E standard：

> XXX

理由：

---

### Verification Harness

- Project：
- Commands：
- Checks：
- JSON：
- Exit codes：
- Temp DataRoot：
- Runtime：

### Unified Verification

Recommended command：

```text
...
```

Expected output：

```text
...
```

### Computer Use Reduction

Estimate：

- CLI / integration：
- Headless：
- UI Automation：
- Manual / Computer Use：

### Installer Readiness

- Can automate custom path：
- Can verify D drive：
- Can automate upgrade：
- Can automate uninstall：
- Remaining manual：

### CI Recommendation

- PR：
- Push：
- Manual / Nightly：
- Release Gate：

### Files

列出所有新增 / 修改文件。

### Tests

- Total：
- Passed：
- Failed：

### Git

- Commits：
- Push：
- Working tree：

### Final Decision

明确回答：

1. Final Check 是否应该正式采用 Avalonia Headless？
2. 是否应该建立 Verification Harness？
3. Windows E2E 推荐什么方案？
4. Computer Use 今后只保留哪些场景？
5. 是否可以从下一阶段开始使用新的验证标准？

不要仅给出“可以考虑”。

必须给明确工程结论。

---

# 执行记录（2026-09-20）

本节记录上述原始任务规范的实际执行证据，不改变原规范。本 Spike 在独立 `codex/verification-infrastructure-spike` 工作树完成，原 `main` 工作树未提交的 Stage A1 内容保持原样。

- Avalonia Headless：`Avalonia.Headless` 12.1.2 + 现有 xUnit 2 的手动 session；真实 MainWindow/ComparisonResultsView XAML、布局、控件树、键盘/输入/命令测试 2/2 通过。完整列表选择链存在一个待后续定位的 `SelectedEntry` 已更新但 `SelectedChange` 变为 null 的现象；没有为了 Spike 修改业务逻辑。
- Windows UI Automation：`FlaUI.UIA3` 5.0.0 驱动**真实 Debug GUI 隔离进程**，找到 `MainQuickCompare`，Invoke 后找到 `ComparisonStart` 并读取 disabled 状态，正常退出和清理；最近一次 5,787 ms。未实测已安装 Test Build、Installer Wizard、FlaUI.Cli 或 Appium/WinAppDriver。
- Verification Harness：在 GUID 临时 DataRoot 中实测 schema 9 / SQLite integrity、两份生成式 DOCX 解析、30→60 比对（10 项）、Snapshot/Comparison 重开、Project + 明确角色版本重开、原件 SHA-256 不变与 DataRoot 无原件。成功 JSON/exit 0、参数错误 JSON/exit 2；单独运行 4,155 ms，统一入口内运行 3,303 ms，成功后默认清理。
- 统一入口：`pwsh -NoProfile -File scripts/verify.ps1` 实测 restore、Release build（0 警告/0 错误）、6 个测试工程 290/290 通过、Harness 8/8 检查通过并返回 `Overall PASS`。
- 架构结论、FlaUI/Appium 取舍、CI 分层及人工边界见 [verification-strategy.md](../development/verification-strategy.md)。本 Spike 不改现有 `.github/workflows/`、业务逻辑、Release 或用户数据库。
