# Final Check — Usability & Workflow Correction v0

> Status: IN PROGRESS
> Version: v0.1.0 development
> Stage: A
> Product baseline: PRD after commit `b8986e2`
> Scope: Comparison Workspace / Workflow Simplification / Automatic Template Matching / Ignore Rules
> Depends on: Comparison UI v0, Project / Template / Version Management v0, Comparison Engine v0, Storage Foundation v0
> Excludes: Installer Wizard, Storage Settings UI, Format Restore UI, Export, Backup/Restore, Three-way Compare, In-app Update

---

# 1. 任务目标

## 实际执行状态（截至 2026-09-23；起始基线 2026-09-14）

- Phase A1：DONE；统一工作台、持久化状态撤销与逻辑滚动协调已实现并完成阶段验收。
- Phase A2 / A3 / A4：NOT STARTED；仅在前一阶段测试、文档、commit / push 全部完成后开始。
- 起始基线：`b8986e2`，main 与 origin/main 一致，工作区原本干净；数据库 schema 9。
- 起始 Release 测试：288/288 PASS（Core 2 / Documents 45 / Comparison 52 / Data 139 / App 50）。
- 尚未完成：A2–A4 及 Stage A 最终 HEAD CI / Test Build / 实际安装验收；A1 的 Debug 隔离数据试用不得替代最终安装包验收。

### A1 安全检查点（2026-09-19，仍为 IN PROGRESS）

- 已实现但未完成 GUI 验收：同屏 Comparison Workspace（清单、双栏、详情/批注/审阅）、分组/逐项选择保持与上一项/下一项、动态审阅统计、inline 撤销；SQLite 对每个 ChangeId 的混合原状态进行原子条件恢复，拒绝覆盖较新外部审阅，冻结 Snapshot/ComparisonResult 不变。
- 联动滚动使用可扩展 panel 列表、Leader/Follower、视口中心已实现节点、33 ms 高频合并、唯一高可信 Mapping、Follower 反馈抑制及重启联动后重对齐；生成式隔离测试样本为基准 400 段、当前 508 段、368 项差异，含大块新增/删除、格式变化及合成批注，不接触用户正常 DataRoot。
- Release 全量测试 296/296 PASS（Core 2 / Documents 45 / Comparison 52 / Data 141 / App 56）；Release/Debug build 均通过、Release 0 warning / 0 error。新增测试覆盖混合组 Undo / 重开、并发冲突不覆盖、历史 raw payload 保留、滚动角色/锚点/未匹配及三 panel 核心结构。
- 待完成：实际窗口慢速/快速鼠标滚轮、滚动条拖动、换侧、关闭/重开联动、连续审阅/Undo/重启以及尺寸响应验收；当前 Windows 锁屏，已暂停 computer-use 界面输入，不能以自动测试冒充通过。GUI 验收完成后才可更新 A1 为 DONE、创建 A1 独立 commit 并正常 push，随后方可开始 A2。
- A4 补充格式 Ignore Rules 已同步至本 task、PRD 和 Comparison architecture；尚未开始代码实现。当前引擎不输出字间距差异，已向用户询问 A4 应先增加稳定检测还是明确暂不支持；不得把任务规范已写入当作能力已交付。

### A1 恢复检查（2026-09-20，仍为 IN PROGRESS）

- 重新读取本 task、AGENTS、校准 PRD、Comparison architecture 与现有实现；`main` / `origin/main` 仍为 `b8986e2`，A1 工作区改动原样保留，A2–A4 未开始。独立 Verification Spike 已在 `codex/verification-infrastructure-spike` 推送，尚未合入本工作树，不把其结果冒充 A1 已安装验收。
- 本机重新运行 Release 全量测试 296/296 PASS；对组内 ListBox 在 ItemsSource 更新时可能把共享 `SelectedChange` 清空的问题，调整为单向选中绑定并只接受非空的真实成员选择；App 测试 56/56 PASS。真实窗口选择仍待复验。
- 当前 Debug 进程使用显式隔离 `--developer-data-directory` 与生成式 fixture，未触碰正常用户 DataRoot。Computer Use 能识别该窗口，但两次激活均返回 `failed to activate captured window`，已停止窗口输入；慢/快滚轮、滚动条、换侧、联动开关、连续审阅/Undo/重启等人工门禁仍未通过。桌面可交互后须重启包含本次修复的 Debug 构建再验收，不能使用旧运行进程结果。
- A1 未标记 DONE，未创建 Phase commit / push；须完成实际 GUI 验收并复跑相关测试，更新证据后才进入 A2。

### A1 恢复检查（2026-09-22，仍为 IN PROGRESS）

- 再次核对 `main` / `origin/main` 为 `b8986e2`，A1 本地改动仍未提交；独立 Verification Spike 分支未合入。使用隔离的 `--developer-data-directory` 重建 400 / 508 段、368 项差异夹具，并从当前 Debug 构建启动桌面窗口，未触碰正常 DataRoot。
- 桌面窗口首次可激活并显示首页，但点击“继续最近一次比对”后窗口变为 minimized；刷新窗口目标后再次激活超时。依 Computer Use 安全规则停止本轮窗口输入；诊断后已停止本轮启动的隔离 Debug 进程（PID 41780），未删除隔离夹具。不能声称已完成真实滚轮、滚动条、换侧、联动开关、连续审阅/Undo/重启或尺寸响应验收。
- 新增 3 个 Avalonia Headless 真实 View/XAML 回归测试，覆盖清单选择保持、组内成员切换逐项视图保持，以及 368 项差异 / 400+508 段预览的虚拟化容器；聚焦测试 3/3 PASS。Release 全量测试 299/299 PASS（Core 2 / Documents 45 / Comparison 52 / Data 141 / App 59），Release solution build 0 warning / 0 error；Headless 测试不能替代上述真实桌面输入门禁。
- Phase A1 仍为 `IN PROGRESS`，无 A1 commit / push；A2–A4 未开始。恢复可交互桌面后须从当前代码重启隔离 Debug 进程，完成真实 GUI 验收、复跑相关测试、记录结果，再按 Phase 规则 commit / push。

### Phase A1 完成验收（2026-09-23）

- 统一工作台同屏显示修改清单、基准/当前双栏、详情/批注、已审阅/忽略/恢复未处理和 Undo；归并/逐项、搜索/筛选、上一项/下一项联动当前成员。修正旧组 ListBox 延迟选择事件可能抢回新成员的问题，并压缩窄窗口概览，默认 1080×680 窗口双栏预览约 298×124 px，最大化后约 567×595 px。
- 使用 GUID 隔离 `--developer-data-directory` 和 Debug-only fixture 生成 400 段基准、508 段当前、368 项修改（含集中插删、格式变化、批注），不访问正常 DataRoot。Windows UI Automation CLI 对实际桌面进程连续两次完整 PASS：选择/定位、导航、审阅/忽略及撤销、搜索/筛选、组/逐项、真实鼠标滚轮/快速滚轮/滚动条拖动、换侧 Leader、联动关闭/重开、审阅后重启持久化且最终恢复未处理。一次中间运行曾暴露上一项偶发未返回，修复选择事件后连续两次复验通过；不把早期失败算作通过证据。
- 限量 Computer Use 可视操作复核了首页进入工作台、默认/最大化布局、慢速及快速鼠标滚轮、滚动条拖动；在不等长文档中观察两侧按段落逻辑推进，没有明显失步。触控板设备未测试，不将其记为已通过。CLI 操作和可视试用均来自实际窗口，不只是模型/Headless 测试；使用的是 Debug 隔离夹具，非最终 Test Build 安装验收。
- `dotnet test FinalCheck.sln -c Release` 301/301 PASS（Core 2 / Documents 45 / Comparison 52 / Data 141 / App 61）；Debug Desktop build 0 warning / 0 error，新增 Avalonia Headless 覆盖组内成员与大量虚拟化条目导航。A2–A4 未开始。

`Phase A1 — DONE`（阶段提交和远程推送完成后方可开始 A2）。

### Stage A Workspace 展示修正（2026-09-23，后续实现进行中）

用户在 A1 commit `c576c85` 已推送后调整默认展示要求：A1 的既有全文双栏与联动滚动保持为已完成基础，不重写已推送历史；在后续 Stage A 工作中增加默认 Context View 与按需 Full Document Mode。本修正不新开 Phase，未通过上下文/模式切换测试和最终包验收前不得宣称新默认已实现。

2026-09-23 实施/验证进展（仍非最终安装包验收）：默认 Context View 已从冻结 PreviewBlock/ChangeItem 投影，选中项只高亮其 span；段落、表格行/单元格、多段落单元格、Move 原/现位置及格式差异已加入测试。Full Document Mode 保留既有全文预览、定位和 Leader/Follower；1080×680 下 Context 将详情/状态操作放在侧边，全文模式改用较宽预览。App Release 定向测试 26/26（含 150 次连续上下文切换）、Debug Desktop build 通过；Windows Debug 隔离夹具 368 项的 CLI 验证最近一次通过 Context ↔ Full、导航、审阅/Undo、全文鼠标滚轮/拖动/联动和重启恢复。布局调整后的前一次 CLI 曾在重新开启联动的时序断言失败，复跑通过；最终 HEAD 安装包仍须完整人工验收，不能用 Debug CLI 替代。

本阶段解决真实试用暴露出的核心问题：

> Final Check 当前底层功能较完整，但用户仍需要理解项目、版本、轮次、基准等内部模型，Comparison 页面也存在修改清单、文档对照、审阅操作割裂的问题。

本阶段的目标不是增加更多复杂概念，而是：

> **让用户提供文件后，Final Check 主动承担项目、版本、模板和比对上下文管理。**

完成后，核心使用流程应接近：

```text
首次使用：

拖入修订合同
→ 自动检查模板
→ 显示推荐基准
→ 开始比对
→ 自动形成项目和版本历史
→ 在同一工作台连续查看差异
```

已有项目：

```text
打开项目
→ 添加新版本
→ 自动编号
→ 自动建议基准
→ 开始比对
→ 历史自动保存
```

---

# 2. 唯一产品需求基准

本阶段必须首先读取：

- `AGENTS.md`
- `docs/PRD/PRD-v0.1.0.md`
- `docs/tasks/project-template-version-management-v0.md`
- Comparison UI task / architecture
- Project / Template / Version architecture
- Comparison Engine architecture
- Storage architecture

特别注意：

> `docs/PRD/PRD-v0.1.0.md` 在 commit `b8986e2` 后的内容，是本阶段唯一有效产品需求基准。

旧 task 中已经 DONE 的交互要求，只作为：

- 历史实现记录；
- 数据兼容参考；
- 测试证据；

不得覆盖新版 PRD。

如旧 task 与新版 PRD 冲突：

> 以新版 PRD 为准。

不要为了兼容旧 UI 而继续保留明显增加操作负担的前置步骤。

---

# 3. 总体原则

## 3.1 隐藏内部复杂度

底层继续保留：

- Project
- Template
- TemplateVersion
- ContractVersion
- ContractRound
- Role
- CurrentBaseline
- ComparisonRecord

但：

> 底层存在，不代表用户必须逐项配置。

能自动完成的事情，应自动完成。

---

## 3.2 不推倒现有稳定数据模型

本阶段主要调整：

- UI；
- Workflow；
- 默认行为；
- 推荐逻辑；
- 自动化编排。

不得因为交互简化直接删除：

- ContractRound；
- Role；
- Baseline；
- 历史 Comparison；
- Snapshot；
- 已有数据库字段。

如确需 schema 增量：

必须采用非破坏式 Migration。

不得重建数据库。

---

## 3.3 用户始终知道当前在比较什么

自动化不能变成黑盒。

开始 Comparison 前，至少明确显示：

```text
当前文件：XXX.docx
比对基准：XXX 模板 v1.2
```

用户可以：

> 更换基准

但默认路径不要求重复选择。

---

# 4. Phase A1 — Comparison Workspace 重构

这是本阶段最高优先级。

---

# 4.1 当前问题

当前修改清单 / 修改详情 / 审阅操作 / 文档对照之间存在割裂。

用户需要：

- 看修改；
- 切文档；
- 找位置；
- 再返回修改详情；
- 再执行状态操作。

这不符合连续合同审阅场景。

---

# 4.2 新 Comparison Workspace

应尽量在同一个主工作区完成：

- 修改清单；
- 搜索；
- 筛选；
- 修改统计；
- 基准文档；
- 当前文档；
- 当前修改详情；
- 批注；
- 状态操作。

默认布局以“修改清单 + 当前修改的 Baseline/Current Context + 详情/批注/状态操作”为主。建议布局可按实际窗口宽度响应式调整，不要求机械照搬某一固定线框。全文双栏作为可返回的按需深度模式。

核心原则：

> 用户选中一条修改后，不需要离开当前工作台完成理解和处理。

---

# 4.3 修改清单

继续支持：

- Grouped
- Individual
- 搜索
- 状态筛选
- 类型筛选

列表项至少明显展示：

- 修改类型；
- 条款 / 位置；
- 摘要；
- 当前状态。

选中后自动更新两侧逻辑上下文、精确高亮、详情和状态；Full Document Mode 中仍自动驱动双栏定位。

---

# 4.4 默认上下文与按需全文

默认 Context View 自动并列或上下排列 Baseline Context 与 Current Context：完整呈现当前条款/段落及必要前后逻辑节点，表格保留 Table/Row/Cell 结构，不按固定字符数截断。删除、新增、替换、Move、Format difference 继续精确高亮；映射不足时诚实说明，不猜测不存在的对应正文。

“查看完整文档 / 全文对照”切换到保留的 Baseline Full Document | Current Full Document，两侧继续有清单定位、差异高亮、表格预览、Leader/Follower 联动及关闭/重开。可以返回 Context View 和原选中 ChangeItem，不要求重新定位。默认上下文不依赖全文长距离联动完成逐项审阅；全文滚动仍按 A1 原门槛验收。

较宽窗口可采用“清单 | 原文上下文 | 现文上下文，下方详情/操作”；较窄窗口可采用“清单 | 上下文区域”，区域内原文/现文上下排列或切换。正常桌面宽度必须可用，不因全文常驻挤窄清单、拆走详情或要求横向滚动。视觉优先级为当前修改、两侧上下文、详情、批注、状态操作、全文浏览。

---

# 4.5 当前修改详情

工作区内显示：

- 原文；
- 修改后；
- 修改类型；
- 条款；
- 位置；
- Format Difference；
- Revision evidence；
- Comment；
- Parsing / confidence 信息（必要时）。

不要让详情成为独立工作流页面。

---

# 4.6 批注

如果修改项有关联批注：

在当前修改详情区域直接展示。

至少包含：

- 内容；
- 作者；
- 时间。

无法关联的批注继续保留独立数据。

独立批注完整视图可后续继续完善，但不得阻塞本阶段。

---

# 4.7 连续审阅

支持：

- 上一项
- 下一项

建议加入快捷键，但不得覆盖常见文本操作快捷键。

切换修改项后：

- Baseline / Current Context 与高亮；
- Full Document Mode 的双栏定位；
- 详情、批注与审阅状态；

全部自动更新。

---

# 4.8 已审阅语义

UI 统一：

- 未处理
- 已审阅
- 忽略

“已审阅”仅表示：

> 已经人工查看。

不得使用会暗示：

- 接受；
- 同意；
- 已处理；

的措辞。

可以在统计中体现：

```text
总变化 86
已审阅 37
未处理 45
忽略 4
```

---

# 4.9 状态 Undo

以下操作必须允许撤销：

## 已审阅

执行后可：

> 撤销 → 恢复原状态

## 忽略

执行后可：

> 撤销

且在显示已忽略时允许恢复。

---

# 4.10 Undo UX

普通状态操作优先：

```text
已标记为已审阅                [撤销]
```

或：

```text
已忽略此修改                  [撤销]
```

使用：

- Snackbar；
- Toast；
- Inline notification；

均可。

不要每次都弹确认框。

永久删除等不可逆操作继续使用确认。

---

# 4.11 状态持久化

Undo 后必须正确保存最终状态。

重启软件后：

显示最终状态。

不得只在 ViewModel 内临时回退。

---

# 5. 联动滚动重构

与 Phase A1 一并完成。

---

# 5.1 Leader / Follower

用户当前主动操作的一侧为：

> Leader

另一侧：

> Follower

Follower 被程序定位时：

不得反过来触发 Leader 再次跳转。

必须避免 feedback loop。

---

# 5.2 Anchor

继续使用逻辑节点：

- Paragraph
- Clause
- Table
- Cell（必要时）

不要做简单像素同步。

---

# 5.3 Viewport Anchor

滚动时根据：

> 当前视口中心附近的主要逻辑节点

确定 Anchor。

Anchor 真正变化时才驱动另一侧。

---

# 5.4 快速滚动

必须处理：

- 鼠标滚轮连续滚动；
- 快速拖动 scrollbar；
- 触控板滚动（测试环境有条件时）。

不要每一个像素事件都执行昂贵同步。

可采用：

- throttle；
- debounce；
- generation token；
- cancellation；

但不得造成明显迟钝。

---

# 5.5 禁用 / 恢复

继续支持：

> 关闭联动

重新打开时：

以当前 Leader 所在逻辑节点重新建立对齐。

---

# 5.6 人工验收

必须实际操作：

- 慢速滚轮；
- 快速滚轮；
- scrollbar 拖动；
- 多处插入 / 删除导致两侧长度明显不同的文档。

不能只用单元测试声明联动体验完成。

---

# 6. Phase A1 验收

至少验证：

- 单击修改项自动双栏定位；
- 上一项 / 下一项；
- 状态已审阅；
- 忽略；
- Undo；
- 重启恢复；
- Grouped / Individual；
- 搜索 / 筛选；
- 双栏联动；
- 联动开关；
- 大量插入后的同步稳定性。

2026-09-23 新默认展示补充（不撤销 `c576c85` 的 A1 既有验收）：后续 Stage A 必须新增自动测试 selection / previous / next → 两侧 Context 更新、Baseline/Current 身份、精确高亮、Paragraph、Table/Cell、Move 来源/目标、Context → Full → Context、review/filter 后选择与 Context 一致；100+ ChangeItems 连续操作不重新解析 DOCX。全文模式保留 A1 的滚动门禁。

完成后：

`Phase A1 — DONE`

建议 commit：

`feat: unify comparison review workspace`

---

# 7. Phase A2 — 自动项目化与版本流程简化

---

# 7.1 首次比对

用户不需要先：

- 创建 Project；
- 建 Round；
- 手填版本号；
- 完成复杂角色配置。

允许直接从新建比对入口提供文件。

---

# 7.2 正式比对成功后自动项目化

正式 Comparison 成功后：

如果当前没有 Project context：

自动：

1. 创建 Project；
2. 保存参与比对的 ContractVersion；
3. 保存 Comparison 与 Project 的关联；
4. 保存 Snapshot / History；
5. 自动生成项目内版本号。

不得要求用户在比对完成后重新导入同样文件。

---

# 7.3 自动版本号

自动生成：

- V1
- V2
- V3
- V4

基于：

> 项目内版本顺序

版本号不得改写原文件名。

同一文件如果因 duplicate 逻辑被重新引用：

不得错误推进版本号或产生冲突。

具体规则应写入 architecture。

---

# 7.4 项目名称

自动项目化时：

生成合理建议值。

优先：

- 模板名称；
- 可可靠取得的合同标题；
- 已明确的对手方；
- 文件名。

无法可靠判断：

使用安全、可理解的默认项目名。

例如：

```text
货运代理协议
```

或：

```text
客户修订版
```

不要猜测无法确认的公司名称。

项目名称可以稍后编辑。

不得阻塞 Comparison。

---

# 7.5 Role 简化

首次比较不依赖 Role 时：

允许：

- 我方；
- 对方；
- 未指定。

不得为了单纯 Comparison 强制要求 Role。

但：

使用“当前我方基准”等功能时：

必须有明确 Own Role。

不得自动猜测 Role。

---

# 7.6 Round 简化

正常导入：

不要求选择：

- 当前轮；
- 下一轮；
- Round Number。

默认按版本时间线管理。

已有 ContractRound：

继续保留。

高级用户以后需要时可整理轮次。

不要删除现有 Round 数据。

---

# 7.7 已有项目添加版本

已有项目：

> 添加新版本

默认自动：

- 生成下一个 Vn；
- 保存 Snapshot；
- 进入比对建议。

用户不需要重新创建 Round。

---

# 7.8 历史兼容

已有旧项目：

- Round；
- Role；
- Baseline；

保持原数据。

新流程不得导致：

- 历史 Version ID 改变；
- Comparison 重新计算；
- Review state 丢失。

---

# 8. Phase A2 验收

至少：

### 场景 A

两份独立 DOCX：

```text
New Compare
→ Comparison
→ Project 自动出现
→ V1 / V2
→ Comparison History 存在
```

### 场景 B

打开自动创建项目：

```text
添加新文件
→ V3
→ Comparison
→ History 追加
```

### 场景 C

重启：

- Project 存在；
- V1/V2/V3 存在；
- History 存在。

### 场景 D

旧 Round Project：

升级后仍能正常打开。

完成：

`Phase A2 — DONE`

建议 commit：

`feat: simplify project and version workflow`

---

# 9. Phase A3 — 自动模板匹配与默认基准

---

# 9.1 已绑定项目

当前 Project 已绑定 Template：

默认：

> 使用该逻辑模板当前启用版本。

Comparison 前只显示：

```text
比对基准：
货运代理协议 v1.2

[更换基准]
```

不得每次打开完整参考合同选择器。

---

# 9.2 项目绑定历史版本

已有项目即使最初绑定：

> v1.0

而 Template 当前版本已经变成：

> v1.2

新的 Comparison 默认：

> 当前启用版本 v1.2

历史 Comparison：

继续保持原 v1.0 Snapshot。

不得重算。

如果 PRD 对 Project 固定模板版本与逻辑模板 current 的最新校准另有明确规则：

严格按最新 PRD 执行。

---

# 9.3 无项目时自动模板匹配

用户拖入 / 选择待比较合同：

自动检查 Template Center。

匹配因素至少评估：

- 文件名；
- 标题；
- 条款结构；
- 条款标题；
- 正文；
- 表格结构。

不得仅靠文件名。

---

# 9.4 Match Result

### Case A — 高可信唯一匹配

自动预选。

显示：

```text
已匹配：
货运代理协议 v1.2

匹配度：高

[更换基准]
```

用户仍需：

> 开始比对

不要自动执行 Comparison。

---

### Case B — 多候选

显示 Top 3：

```text
1. 货运代理协议 v1.2        92%
2. 货运代理协议 v1.1        89%
3. 综合物流服务协议 v2.0    73%
```

显示：

- 名称；
- Version；
- Current / Historical；
- Score。

默认预选 Top 1。

用户确认即可。

---

### Case C — 无可靠匹配

提供：

> 手动选择参考合同 / 模板

不要硬匹配。

---

# 9.5 匹配算法

MVP 不要求 AI。

应使用确定性、本地算法。

建议结合：

- normalized title similarity；
- clause heading overlap；
- structural sequence similarity；
- paragraph fingerprints；
- body similarity；
- table structure fingerprints。

必须控制性能。

不能每次拖一个文件就完整解析所有历史模板而卡死 UI。

应利用：

- 已保存 Snapshot；
- 预计算特征；
- lazy loading；
- background worker。

---

# 9.6 Current Version 权重

Template 当前启用版本：

匹配时提高权重。

Historical：

仍参与匹配。

不能完全排除历史版本。

---

# 9.7 更换基准

用户点击：

> 更换基准

才展示完整选项，例如：

- Current template
- Historical templates
- Project versions

不要把高级选择作为默认第一步。

---

# 10. Phase A3 验收

至少准备：

- 一个唯一匹配样本；
- 两个高度相似模板；
- 一个完全不匹配合同；
- 一个历史版本与 current 相近模板；
- 已绑定项目；
- 模板 current version 更新后的项目。

验证：

- 唯一匹配；
- Top 3；
- current 权重；
- historical 参与；
- 无匹配 fallback；
- 不自动 Comparison；
- 项目绑定模板无需重复选基准；
- 历史 Comparison 不变化。

完成：

`Phase A3 — DONE`

建议 commit：

`feat: add automatic template matching and baseline defaults`

---

# 11. Phase A4 — Comparison Ignore Rules

---

# 11.1 目标

允许用户在一次 Comparison 中暂时不显示某类低价值差异。

第一版至少支持：

- 标点变化；
- 页码变化；
- 序号 / 编号变化；
- 指定字符；
- 自定义字符集合。

---

# 11.2 两类 Ignore 必须分离

### Comparison Ignore Rule

表示：

> 比对展示 / normalization 规则。

例如：

```text
忽略标点差异
```

### Change Review Ignore

表示：

> 用户已经看到一条变化，主动将该 ChangeItem 标记为忽略。

二者：

不得共用：

- 状态枚举；
- 数据库字段；
- UI 文案。

---

# 11.3 默认 Scope

默认：

> 本次 Comparison

不要一打开开关就改变所有项目。

---

# 11.4 UI

开始 Comparison 前可提供：

> 比对选项

默认折叠，避免增加主流程负担。

例如：

```text
比对选项 ▼

☐ 忽略标点符号变化
☐ 忽略页码变化
☐ 忽略序号变化

自定义忽略字符：
[                       ]
```

用户不用该功能时：

不增加明显操作步骤。

---

# 11.5 标点

需覆盖常见：

### 中文

- ，
- 。
- ；
- ：
- ！
- ？
- （）
- 【】
- 《》
- “”
- ‘’

### English

- ,
- .
- ;
- :
- !
- ?
- ()
- []
- quotes

不要直接删除所有 Unicode punctuation 而不测试。

需要避免：

> 标点实际承载法律含义时无法恢复查看。

所以原始差异必须保留。

---

# 11.6 页码

“忽略页码”不能简单理解为：

> 删除所有数字。

需要识别典型 page number / page field / standalone page marker。

避免把：

- 金额；
- 天数；
- 日期；
- 条款号；

错误忽略。

如果当前 DOCX Snapshot 无法可靠判断 Word Page field：

明确写入限制。

---

# 11.7 序号

忽略：

- 1.
- 1、
- （1）
- ①
- A.
- a)
- 一、
- （一）

等编号变化时：

需要尽量基于 numbering / paragraph structure。

不得全局删除数字和字母。

---

# 11.8 自定义字符

用户允许填写：

> 忽略字符集合

例如：

```text
，。；：
```

需明确：

这是：

> character-level ignore set

不是 regex。

若未来支持 regex：

另行设计。

---

# 11.9 历史可追溯

ComparisonRecord 应保存：

> 本次使用的 Ignore Rules。

历史打开时：

能知道本次 Comparison 使用了什么规则。

最好允许：

> 查看完整差异 / 查看过滤结果

如果当前架构更适合保存完整 raw result + view filter：

优先该方案。

不要永久丢弃变化事实。

---

# 11.10 补充要求（2026-09-14）：格式差异忽略

本次 Comparison 的“比对选项”必须分为内容与格式类别，支持一键“忽略全部格式变化”以及单独选择格式类型。未选中的格式类型和文字内容变化继续正常显示；混合格式项只隐藏选中的属性，不隐藏整个项中剩余差异。

至少提供字体（全部字体槽）、字号、字体颜色、加粗、斜体、下划线、删除线、高亮、段落对齐、左/右/首行/悬挂缩进、段前/段后间距、行间距/行距规则，以及当前引擎已稳定识别的其他字符/段落属性。字间距 / character spacing 是正式要求，但若现有模型/引擎未支持，应明确显示未支持或记录限制；不得为了 Ignore Rules 临时增加未验证的格式检测。

已稳定分类的表格/单元格属性也提供独立控制，例如对齐、边框、底色、宽度；行高/列宽仅在现有引擎已可靠输出相应分类时启用，不将结构变化冒充格式变化。

格式配置独立于人工 Ignored、Confirmed / 已审阅和 Format Restore 状态，作用域仅本次 Comparison。ComparisonRecord 保存当时选择的全部/具体格式忽略配置；历史可说明哪些属性被隐藏，并切换“显示完整差异”。保存完整 raw format differences / 原始 ChangeItem / spans，不重写、不删除底层事实，不修改 Snapshot 或映射。Format Restore 始终使用未过滤的原始结果，即使 UI 隐藏“字体：宋体 → 仿宋”，其分析/恢复基础仍保留。

A4 增加测试：只隐藏字体/字号时行距保留；只隐藏段间距/行距时字体保留；忽略全部格式时文字保留；混合项剩余属性；表格格式与内容/结构分离；配置历史恢复/关闭规则恢复全部事实；过滤前后 raw result 与 Format Restore 输入不变。以上是 A4 新增验收，不重做已完成阶段。

---

# 12. Phase A4 验收

至少验证：

### 标点

```text
甲方，应付款。
甲方,应付款
```

开关前后表现正确。

### 页码

页码变化不影响正文数字。

### 序号

```text
1. 付款
2. 责任
```

编号变化可忽略，正文变化仍显示。

### 自定义字符

只隐藏指定字符变化。

### History

重启后：

Comparison Ignore Rules 可追溯。

完成：

`Phase A4 — DONE`

建议 commit：

`feat: add configurable comparison ignore rules`

---

# 13. 本阶段不实现

明确不做：

- Three-way Compare；
- PDF；
- OCR；
- Format Restore UI；
- Style Library；
- Installer Wizard；
- Storage Settings；
- Excel Export；
- TXT / Markdown Export；
- Global Search；
- Backup / Restore；
- In-app Update；
- Stable Release。

不得顺手扩大范围。

---

# 14. Format Restore 兼容

虽然本阶段不做 Format Restore UI，但 Comparison Workspace 设计时：

必须预留未来 ChangeItem 上：

- format restore action；
- restore state；
- undo restore；

合理位置。

不要做完 A1 后导致 Stage C 必须重新拆整个布局。

---

# 15. Three-way Compare 兼容

虽然不实现 Three-way Compare：

Comparison Workspace 不应硬编码只能存在两个 document panel 的核心数据结构。

UI 当前可以只呈现两栏。

但 ViewModel / workspace architecture 应避免：

> 把双栏数量写死到不可扩展。

不要求提前实现第三栏。

只是保持可扩展性。

---

# 16. 性能

本阶段特别关注 UI 性能。

至少验证：

- 400 paragraph comparison；
- 100+ ChangeItems；
- 连续上一项 / 下一项；
- 快速滚动；
- Search；
- Filter；
- Template matching against 20+ TemplateVersions。

UI 不应明显冻结。

耗时操作：

- parsing；
- hash；
- matching；
- comparison；

后台执行。

---

# 17. Accessibility / 易发现性

按钮不要过多、过大。

遵循项目现有 UI 原则：

- 主要动作明显；
- 次级动作弱化；
- 不过度留白；
- 不使用大量分割线；
- 不用内部术语轰炸用户。

例如：

优先：

> 更换基准

不要直接展示：

> Baseline Type / Snapshot Identity / TemplateVersionId

---

# 18. 数据 Migration

如新增：

- auto project metadata；
- auto version sequence；
- template match feature cache；
- comparison ignore config；

使用 EF Core 增量 migration。

必须测试：

> 当前生产开发数据库从现有最新 schema 原位升级。

历史数据：

全部保留。

---

# 19. Tests

不得降低现有覆盖。

新增至少覆盖：

## Workspace

- selection → navigation
- previous / next
- reviewed undo
- ignored undo
- persistence
- filter interaction

## Scroll

- leader/follower
- feedback-loop suppression
- anchor change
- enable/disable

## Auto Project

- no project → create
- V1/V2
- append V3
- restart
- duplicate handling
- old project compatibility

## Template Match

- unique
- Top 3
- no match
- current weighting
- historical participation
- bound project

## Ignore Rules

- punctuation
- page
- numbering
- custom chars
- original changes preserved
- history persistence

---

# 20. 实际安装验收

全部 Phase 完成后：

从最终 HEAD 创建新的 Test Build。

必须使用真正安装后的程序手工验证。

不得用：

- unit test；
- Debug run；
- old Test Build；

替代最终验收。

---

# 21. 手工验收样本

至少准备一组合成 / 脱敏 DOCX：

### Template

标准合同。

### Version A

有：

- 文本修改；
- 标点变化；
- 格式修改；
- 批注。

### Version B

进一步：

- 新增条款；
- 删除；
- 编号变化；
- 表格修改。

---

# 22. 手工验收流程

## Test 1 — New Compare

直接拖入文件：

- 不先创建 Project；
- Template 自动匹配；
- 显示推荐基准；
- Start Comparison；
- 自动项目化。

---

## Test 2 — Workspace

连续：

- 选择修改项；
- 上一项；
- 下一项；
- 自动更新两侧完整逻辑上下文、精确高亮、详情、批注和状态；
- Review；
- Ignore；
- Undo。

用文本修改、整段重写、条款移动、表格单元格、批注和长合同，在窄窗与最大化窗口分别试用；判断是否无需频繁滚动全文即可连续审阅。打开“全文对照”，核对当前项定位，再返回 Context View，选择与审阅状态保持一致。

---

## Test 3 — Full Document Scroll

实际鼠标：

- 慢滚；
- 快滚；
- scrollbar；
- disable sync；
- enable sync。

---

## Test 4 — Project

重新打开自动项目：

- V1/V2；
- 添加 V3；
- Comparison History；
- restart。

---

## Test 5 — Ignore Rules

分别测试：

- punctuation；
- page；
- numbering；
- custom chars。

验证正文金额 / 日期不得被误忽略。

---

# 23. Git / Phase Discipline

每个 Phase：

```text
实现
→ 测试
→ 更新 task
→ 更新 architecture
→ commit
→ push
→ 确认远端
→ 下一 Phase
```

禁止把 A1-A4 全部压成一个巨大 commit。

---

# 24. 推荐 Commit

### A1

`feat: unify comparison review workspace`

### A2

`feat: simplify project and version workflow`

### A3

`feat: add automatic template matching and baseline defaults`

### A4

`feat: add configurable comparison ignore rules`

最终文档收尾可另：

`docs: record usability workflow correction acceptance`

---

# 25. Git 安全

禁止：

- force push
- reset --hard
- 修改已推送 commit
- 删除历史 migration
- Tag
- Release

当前仍是：

> v0.1.0 development

---

# 26. 最终报告

完成后按以下格式：

## Usability & Workflow Correction v0

### Phase A1 — Comparison Workspace

- Unified workspace：
- Selection sync：
- Previous / next：
- Review：
- Ignore：
- Undo：
- Comments：
- Search/filter：

### Linked Scrolling

- Leader/Follower：
- Anchor：
- Fast scroll：
- Disable/re-enable：
- Manual trial：

### Phase A2 — Workflow Simplification

- Auto project：
- Auto version：
- V1/V2/V3：
- Role simplification：
- Round simplification：
- Old project compatibility：

### Phase A3 — Template Matching

- Bound project baseline：
- Unique match：
- Top 3：
- No match：
- Current weighting：
- Historical templates：
- Performance：

### Phase A4 — Ignore Rules

- Punctuation：
- Page number：
- Numbering：
- Custom chars：
- Raw difference preservation：
- History：

### Database

- Previous schema：
- Current schema：
- Migration：
- Existing data：

### Tests

- Total：
- Passed：
- Failed：

### Performance

- Workspace：
- Matching：
- Large comparison：

### CI

- Windows：
- macOS：

### Test Build

- Version：
- Commit：
- Run ID：

### Installed Manual Trial

- New Compare：
- Auto project：
- Template match：
- Review/Undo：
- Scroll：
- Ignore rules：
- Restart：
- Existing project：

### Git

- Commits：
- Push：
- Working tree：

### Known Limitations

明确写出：

- Installer Wizard 尚未实现；
- Format Restore UI 尚未实现；
- Three-way Compare 尚未实现；
- PDF 不支持；
- Export / Backup / Updater 尚未完成。

最终判断：

> Stage A 是否已使 Final Check 的核心双文件合同审阅流程达到明显降低配置负担、可以连续审阅、无需理解底层项目模型即可完成工作的水平。
