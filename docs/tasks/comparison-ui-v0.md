# Final Check — Comparison UI v0

> Status: Phase 1–5 DONE (implementation / phase verification)  
> Version: v0.1.0 development  
> Scope: First usable comparison workflow  
> Depends on: Document Engine v0, Comparison Engine v0, Format Restore Engine v0, Storage Foundation v0  
> Last updated: 2026-09-13

---

## 1. 任务目标

建立 Final Check 第一版正式、面向用户的 **Comparison UI**。

本阶段需要形成第一条完整可使用流程：

```text
选择 / 拖入基准 DOCX
        +
选择 / 拖入当前 DOCX
        ↓
文件校验
        ↓
解析 DocumentSnapshot
        ↓
执行 Comparison Engine
        ↓
保存 Comparison Result
        ↓
进入比对结果页
        ↓
查看 / 筛选 / 搜索 / 定位修改
```

本阶段完成后，用户应能够实际拿两份 DOCX 在 Final Check 中完成一次独立比对并查看结果。

---

# 2. 产品定位

Comparison UI v0 是：

> Final Check 的第一版真正可试用业务功能。

但它仍不是完整合同项目管理系统。

本阶段采用：

> Quick Compare / 独立比对

作为入口。

暂不要求用户先创建完整 Contract Project、模板、轮次、版本链。

后续项目管理可以复用本阶段已经建立的 Comparison UI。

---

# 3. 开始前必须读取

执行前重新读取：

1. `AGENTS.md`
2. `docs/tasks/comparison-ui-v0.md`
3. `docs/tasks/document-engine-v0.md`
4. `docs/tasks/comparison-engine-v0.md`
5. `docs/tasks/format-restore-engine-v0.md`
6. `docs/tasks/storage-foundation-v0.md`
7. `docs/PRD/PRD-v0.1.0.md`
8. `docs/architecture/document-engine.md`
9. `docs/architecture/comparison-engine.md`
10. `docs/architecture/format-restore-engine.md`
11. `docs/architecture/storage-and-paths.md`
12. 当前 Avalonia App / Desktop / ViewModel / DI / Navigation 实现

不得绕过现有 Engine 重新在 UI 层实现 DOCX Diff。

---

# 4. 多设备执行规则

继续严格遵守 `AGENTS.md`。

每个 Phase：

1. 实现；
2. 测试；
3. 更新 task；
4. 独立 commit；
5. 正常 push；
6. push 成功后进入下一 Phase。

---

# 5. 本阶段明确不做

本轮不扩大到：

- 完整 Template Center
- 自动 Top 3 模板匹配
- 完整 Contract Project
- 多轮谈判时间线
- 我方 / 对方多版本管理
- Format Restore 正式完整 UI
- Excel 导出
- 全局搜索
- Backup / Restore UI
- Storage Settings UI
- Installer Wizard
- AI
- Stable Release

但 UI 架构必须能够被后续项目/模板功能复用。

---

# 6. Phase 1 — Quick Compare Entry / Session Model

建立正式的 Quick Compare 入口和 UI session model。

---

## 6.1 首页

当前首页增加清晰的主入口：

> 新建比对

进入 Comparison Setup 页面。

不要把大量未来功能按钮全部提前堆到首页。

---

## 6.2 Comparison Session

建立 UI / Application 层的：

`ComparisonSession`

或同等模型。

至少保存：

- SessionId
- Baseline file
- Current file
- Baseline metadata
- Current metadata
- Parse status
- Comparison status
- Comparison result ID
- StartedAt
- CompletedAt
- Error / diagnostic state

不要把整个 UI 状态直接塞入 View code-behind。

---

## 6.3 文件角色

Quick Compare 明确只有两侧：

### 左侧

`基准版本`

### 右侧

`当前版本`

后续项目模式中可对应：

- 标准模板 vs 当前版本
- 我方基准 vs 对方版本

但当前 UI 不使用复杂合同项目术语。

---

## 6.4 文件输入

两侧均支持：

- 点击选择 DOCX
- Drag & Drop DOCX

每侧一次只选一个文件。

MVP 只接受：

`.docx`

不接受：

- `.doc`
- `.pdf`
- 图片
- 其他文件

---

## 6.5 Drop Zone

左右两个明显但不过度巨大的文件区域：

```text
基准版本                    当前版本

[ 拖入或选择 DOCX ]        [ 拖入或选择 DOCX ]
```

选择后显示：

- 文件名
- 路径
- 文件大小
- 修改时间
- SHA-256 状态（无需显示完整 hash）
- 替换文件按钮
- 移除按钮

---

## 6.6 开始比对

只有两份文件均通过基础校验后：

`开始比对`

按钮才可用。

不要允许点击后才发现另一侧为空。

---

## Phase 1 验收

至少：

- 首页入口；
- 两侧选择；
- 两侧拖放；
- 非 DOCX 拒绝；
- 替换；
- 删除；
- 路径显示；
- Session state；
- Avalonia UI 测试 / ViewModel 测试。

完成：

`Phase 1 — DONE`

建议 commit：

`feat: add quick comparison entry workflow`

---

# 7. Phase 2 — Validation / Parse / Execution UX

建立完整执行流程。

---

## 7.1 文件预检查

开始 Comparison 前检查：

- 文件存在；
- 可读；
- 后缀；
- hash；
- 是否两个路径完全相同；
- 是否两个文件 hash 相同；
- 是否已变化。

---

## 7.2 相同文件

如果 SHA-256 完全相同：

明确提示：

> 两个文件内容完全一致。

允许：

- 取消；
- 仍然执行比对。

如果仍执行，预期得到：

> 0 changes。

---

## 7.3 解析状态

执行期间 UI 需要显示真实阶段，例如：

```text
正在读取基准文档…
正在读取当前文档…
正在匹配文档结构…
正在比较文字…
正在比较格式…
正在处理修订和批注…
正在整理修改结果…
```

使用已有 Engine Progress。

禁止伪造随机百分比。

---

## 7.4 Progress

如果现有 Engine 能提供可信阶段进度：

可以显示阶段 + progress。

否则：

使用 indeterminate progress + 阶段名称。

---

## 7.5 Cancel

比对过程必须提供：

`取消`

调用已有：

`CancellationToken`

取消后：

- 不显示为失败；
- 不留下半完成 Comparison Result；
- 可以重新执行。

---

## 7.6 Error UX

必须把 Engine Error 转换成用户可理解的错误。

至少区分：

- 文件已移动 / 删除
- 无权限
- 无效 DOCX
- 真正加密 DOCX
- 部分解析
- Comparison cancelled
- Unexpected error

禁止直接把 exception stack trace 当用户消息。

详细信息可以：

`查看技术详情`

---

## 7.7 Partial Parsing

如果文档可以部分解析：

必须提示：

> 本次比对结果可能不完整。

并显示 Diagnostics 摘要。

用户可以：

- 继续查看
- 返回

不能默默隐藏 Partial 状态。

---

## 7.8 Persistence

成功后将 Comparison Result 保存到现有 DataRoot / SQLite。

Quick Compare 即使暂时没有完整 Contract Project：

也必须有独立可追踪记录。

不要让 Comparison Result 只活在内存里。

---

## Phase 2 验收

至少：

- 正常比对；
- 相同文件；
- Invalid DOCX；
- Encrypted；
- Partial；
- Cancel；
- Comparison persistence；
- App 不崩溃。

完成：

`Phase 2 — DONE`

建议：

`feat: add comparison execution experience`

---

# 8. Phase 3 — Result Overview / Change List

成功比对后进入正式结果页。

---

## 8.1 顶部信息

显示：

- 基准文件名
- 当前文件名
- 比对时间
- 总修改数

并给出简洁统计：

- 文字修改
- 格式修改
- 新增
- 删除
- 移动
- 批注

不要塞过多技术信息。

---

## 8.2 Change List

核心区域显示修改列表。

默认：

> Grouped View

允许切换：

> Individual View

使用已有 Comparison Group / ChangeItem。

禁止 UI 再重新归并数据。

---

## 8.3 每个修改项

默认摘要至少显示：

- 类型
- 位置 / Paragraph / 条款提示
- 修改摘要
- 状态
- 数量（Group）

例如：

```text
文字修改

付款期限
30日 → 60日

共 1 处
```

---

## 8.4 展开详情

点击修改项后可看到：

- Original
- Current
- Difference highlight
- Formatting changes
- Native revision
- Comment
- Match confidence
- Diagnostics（必要时）

技术字段默认不要全部展示。

---

## 8.5 Text Highlight

使用 Comparison Engine `DifferenceSpan`。

显示：

```text
原：付款期限为 30 日
现：付款期限为 60 日
```

仅高亮真正发生变化的：

`30` / `60`

不要整段全部标色。

---

## 8.6 Format Change

格式变化明确展示，例如：

```text
字体：宋体 → 仿宋
字号：小四 → 五号
对齐：左对齐 → 两端对齐
```

同一位置多个格式属性：

作为一个修改项展开。

---

## 8.7 Move

ParagraphMove 显示：

- 原位置
- 新位置

Move + Modify 同时：

- 展示移动；
- 展示文字变化。

---

## 8.8 Low Confidence

Medium / Low confidence 必须明显但不过度惊扰地提示：

> 匹配可信度较低，请人工确认。

不要显示为完全确定的变化。

---

## Phase 3 验收

至少：

- 0 changes；
- 单修改；
- 多修改；
- grouped / individual；
- text difference；
- format difference；
- move；
- revision；
- comment；
- low confidence。

完成：

`Phase 3 — DONE`

建议：

`feat: add comparison results and change list`

---

# 9. Phase 4 — Filter / Search / Status

将现有 PRD 中修改管理能力接入 UI。

---

## 9.1 状态

ChangeItem UI 状态：

- 未处理
- 已确认
- 忽略

默认新变化：

`未处理`

---

## 9.2 忽略

被忽略变化：

默认隐藏。

提供：

`显示已忽略`

过滤器。

可以恢复状态。

---

## 9.3 已确认

已确认仍默认显示。

提供快捷：

`仅看未处理`

---

## 9.4 Group 状态

Group 中：

- 全未处理 → 未处理
- 全确认 → 已确认
- 部分确认 → 部分已确认
- 全忽略 → 默认隐藏

Group / Individual 状态必须同步。

---

## 9.5 Filters

至少支持：

### Status

- All
- 未处理
- 已确认
- 已忽略

### Type

- 文字
- 格式
- 新增
- 删除
- 移动
- 表格
- 批注

### Format Restoration

当前 Format Restore UI 尚未正式实现。

如果已有底层状态：

可显示：

- 未恢复
- 已恢复

但不要因此扩大本轮范围。

---

## 9.6 Search

当前 Comparison 内搜索：

- Original text
- Current text
- Clause / heading hint
- Comment

即时或防抖搜索。

---

## 9.7 Counts

Filter 后显示：

> 23 / 61 项

用户应知道当前看到的是过滤结果，不是全部结果。

---

## 9.8 Persistence

用户状态：

- 已确认
- 忽略

必须持久化。

重启后仍然存在。

---

## Phase 4 验收

至少：

- 状态切换；
- persistence；
- Group 同步；
- filter；
- search；
- ignored hidden；
- only unresolved。

完成：

`Phase 4 — DONE`

建议：

`feat: add comparison filtering and review states`

---

# 10. Phase 5 — Document Preview / Navigation / Product Polish

建立第一版文档上下文查看能力。

---

## 10.1 Preview 目标

Comparison UI v0 不要求像 Word 一样 100% 高保真。

优先：

> 内容准确、修改位置准确、点击可定位。

---

## 10.2 双栏预览

建议：

```text
基准版本               当前版本
────────                ────────
正文……                  正文……
正文……                  正文……
```

能够显示：

- Paragraph
- 基础文字格式
- Table 基础结构
- Change highlight

---

## 10.3 Layout

结果页建议提供：

```text
┌────────────────────────────────────────────┐
│ 比对概要 / Filters / Search               │
├───────────────┬────────────────────────────┤
│ 修改列表      │ 文档对照                   │
│               │ Baseline | Current         │
│               │                            │
└───────────────┴────────────────────────────┘
```

窗口较小时允许自适应。

不要把所有内容硬挤在固定宽度。

---

## 10.4 Change Navigation

点击 ChangeItem：

必须让右侧 Preview 定位到对应：

- Paragraph
- Table / Cell

并突出显示。

这是本阶段核心体验。

---

## 10.5 Scroll Linking

使用 PRD 已确定方式：

> 基于匹配 Paragraph / Clause 的逻辑联动。

不是像素级同步。

允许：

- 联动开启
- 暂时解除联动

重新开启时：

以当前附近匹配节点重新对齐。

---

## 10.6 Virtualization

长合同必须避免：

> 一次创建全部复杂 UI 元素。

Change List 和 Preview 尽量使用虚拟化 / 按需加载。

---

## 10.7 Empty / Loading / Error States

必须完善：

- 尚未比对
- 正在比对
- 0 修改
- Partial result
- Error
- Filter 结果为空

避免空白界面。

---

## 10.8 Keyboard / Basic Accessibility

至少：

- Tab 顺序合理；
- Enter 可触发主要操作；
- Esc 可关闭 Dialog / 取消适当操作；
- Tooltip；
- 基础 accessible name。

---

## 10.9 About / Existing Navigation

不得因为 Comparison UI 改造：

- 破坏 About；
- 破坏 Storage；
- 破坏现有 Navigation；
- 破坏跨平台构建。

---

## Phase 5 验收

至少：

- 双栏 Preview；
- Change click 定位；
- Highlight；
- Logical linked scroll；
- Unlink；
- long document basic performance；
- window resize；
- existing navigation regression。

完成：

`Phase 5 — DONE`

建议：

`feat: add comparison document preview`

---

# 11. UI 架构

遵循：

MVVM

UI 不直接调用：

- Open XML SDK
- DbContext
- Comparison implementation

使用 Application Services / interfaces。

---

# 12. Comparison Application Service

如当前缺少合适上层编排服务：

建立类似：

`IComparisonWorkflowService`

负责：

```text
File validation
→ Hash
→ Parse
→ Compare
→ Persist
→ Return UI result
```

UI ViewModel 不应该自行编排 8 个底层 service。

---

# 13. 文件不复制

Quick Compare 输入的原始 DOCX：

继续只记录：

- path
- hash
- metadata

不要复制进 DataRoot。

---

# 14. 外部修改

如果用户选择文件后、点击 Comparison 前文件 hash 已变化：

重新读取 metadata/hash。

如果 Comparison 已完成以后源文件变化：

历史 Comparison 仍基于原 Snapshot。

不得偷偷改变已有结果。

---

# 15. 当前 Comparison Record

Quick Compare 也需要正式历史对象。

至少记录：

- Baseline file
- Current file
- hashes
- Snapshot IDs
- Comparison Result
- comparison time
- review states

后续完整 Project 可将它关联进项目。

不要设计成以后无法迁移的一次性临时表。

---

# 16. 用户界面文案

使用面向普通职场用户的中文。

避免直接暴露：

- Snapshot
- NodeMapping
- LCS
- schema
- OPC
- OpenXML

技术详情页面除外。

---

# 17. 性能目标

至少用：

### Small

10–30 段。

### Medium

300–500 段。

测试：

- 打开 setup 页；
- 文件加载；
- Comparison；
- 结果页进入；
- list scrolling；
- change navigation。

避免 UI 把 Engine 的 20–300 ms 算法优势浪费成数秒卡顿。

---

# 18. 不阻塞 UI Thread

以下操作必须后台执行：

- Hash
- DOCX parse
- Comparison
- Persistence heavy work
- 大型 preview generation

UI 保持响应。

---

# 19. Cancellation / concurrency

用户开始一个 Comparison 后：

禁止重复点击产生多个并行任务。

Cancel 后：

可以重新开始。

离开页面时：

明确决定后台任务是取消还是保持。

第一版建议：

> 离开 setup/result workflow 时，如仍在执行则提示并取消。

---

# 20. Tests

继续保持现有 Engine 197 tests 不回归。

新增：

- ViewModel tests
- Workflow Service tests
- UI state tests

必要时加入 Avalonia headless tests。

不要为了 UI 测试把所有控件做 brittle pixel assertion。

优先测：

- state
- command availability
- navigation
- filter/search
- persistence

---

# 21. 手工测试

本阶段结束后必须通过 Test Build Action 生成最新安装包。

实际安装最新 HEAD 对应 Test Build。

至少手工验证：

1. 安装；
2. 启动；
3. 新建比对；
4. 选择两份非敏感 DOCX；
5. 开始比较；
6. 查看结果；
7. 搜索；
8. Filter；
9. 标记已确认；
10. 重启；
11. 状态仍存在；
12. 卸载。

本轮仍不创建 Tag / Release。

---

# 22. 测试安装包

必须确认 Test Build 对应：

> 当前最终 HEAD

不能像上一阶段出现：

> 安装包早于最终一致性提交。

最终报告中明确：

- Artifact version
- Commit SHA
- Run ID

---

# 23. 文档

新增：

`docs/architecture/comparison-ui.md`

至少说明：

- Quick Compare workflow
- ComparisonSession
- Workflow service
- ViewModel structure
- Result list
- Review state
- Preview
- Navigation
- Virtualization
- Error/partial UX

更新：

- development README
- task 状态

必要时更新 PRD 中已实际落地的交互细节。

---

# 24. 当前 Phase 状态

- Phase 1 — Quick Compare Entry：DONE
  - 实际完成（2026-09-13）：首页新建比对、双侧 DOCX picker / drag-drop 适配、单文件限制、后台只读 metadata/hash 校验、替换/移除、路径与哈希状态、ComparisonSession 与 VM 状态。View 不访问引擎/SQLite；原文件不复制、不修改。Phase 1 仅完成输入基础，执行服务在 Phase 2 接入。
  - 验证：App VM 6/6（新增 4），真实文件 inspector 2/2；Release Desktop 编译 0 warning / 0 error；原有全量 197 加新增 6 的回归检查通过后提交。
- Phase 2 — Execution UX：DONE
  - 实际完成：Core 工作流接口与 Desktop 编排服务调用既有 parser / Comparison Engine；后台重新检查路径/hash/外部变化，持有只读源流并校验解析身份；相同路径/hash 提供继续/取消。真实阶段进度、不定进度条、取消/重复执行 gate、导航离开取消确认、错误分类与 Partial 显式继续。
  - 数据：schema 4 增量 ComparisonRecords 与 FK 引用，两份冻结快照 + 结果 + record 单一事务追加；短 scope 释放 storage lease。Readonly bootstrap inspector、迁移 logical digest / 引用校验、原文排除与 Comparison 逻辑占用同步更新；旧 schema 3 payload 保留。原始 DOCX 不复制。
  - 验证：新增 9 项真实工作流/迁移/升级集成测试及 6 项 VM 执行测试；全量 Release 218 项回归，Desktop build 零警告/零错误。结果列表仍属于 Phase 3；本阶段不宣称完整 UI 已可试用。
- Phase 3 — Results：DONE
  - 实际完成：正式结果页顶部文件/时间/统计、Engine 原 Groups 默认归并与逐项切换、同一原 ChangeItem 的位置/摘要/展开、多属性格式差异、Word 修订与批注证据、Medium/Low 与 Partial 提示；DifferenceSpan UTF-16 精确双侧高亮，不在 UI 重做 diff / grouping。正文/表格位置来自 Snapshot 节点索引，不编造 Word 页码。首页可后台恢复最近记录。
  - 验证：新增 6 项 Results VM 测试（0 修改、归并/逐项共享、数字/UTF-16 高亮、多属性格式/移动、Partial/低可信、修订/批注）；Release 224/224 回归通过，Desktop 构建零警告/零错误。GUID Debug 数据目录实例的实际窗口首页与双侧输入观察通过；最终安装包试用仍在全部阶段后执行。
- Phase 4 — Filter / Review：DONE
  - 实际完成：状态/类型交集筛选、两侧全文/位置/批注即时搜索、原变化计数；默认隐藏忽略并保留确认，忽略可显示/恢复、仅看未处理；归并/逐项共享原 ChangeId 状态，组摘要包含全部成员而批量操作只影响当前显示成员。后台短 scope 保存，数据库 compare-and-swap 合并竞争写入，成功后更新 UI，失败不伪装成功；原 Snapshot / Comparison payload 不变。
  - 验证：新增 6 项 review VM 测试、2 项真实 SQLite 集成测试（新 scope 恢复、冻结 payload 不变、并发不同 ChangeId 合并、非法/取消保护）；Release 232/232 通过，Desktop build 零警告/零错误。重启窗口的实际状态恢复试用纳入最终安装包验收。
- Phase 5 — Preview / Navigation：DONE
  - 实际完成：后台生成准确 Snapshot 原生双栏预览，SourceIndex 正文/表格交错、基础 effective 字体/字号/颜色/粗斜体/下划线/删除线/高亮，Engine span 高亮、段落/Run/Table/Cell 点击定位并突出目标；原 Mapping 逻辑滚动、解除/重开按附近可靠节点对齐，无低可信猜测。清单/预览/组成员虚拟化，筛选集合批量发布，预览/详情页签、弹性布局与 sidebar 对比度、导航期间异步加载不强制跳回。
  - 验证：新增 6 项 preview VM 测试与 20/400 段真实工作流性能测试；Release 240/240 通过，Desktop build 零警告/零错误。生成式非敏感 20/400 段（41/801 项）Debug 实际窗口选择/执行/预览/表格定位/滚动联动/解除/重开及最大化/还原观察通过。20 段完整工作流 333.8 ms，400 段 953.2 ms；4000 段模型后台投影测试通过。
  - 最终外部验收门禁：本提交记录的是已观察源码阶段状态；最终 HEAD 的 Windows/macOS CI 和 Test Build 下载/实际安装/搜索/筛选/确认/重启状态恢复/Portable/卸载尚需待最终 HEAD 推送后执行，不预写 PASS。以该 HEAD 对应 Actions 及当次最终报告记录 Artifact version、SHA、Run ID；不在 dispatch 后为 Run ID 再提交导致包落后。失败必须如实报告或修正后重新验证最终 HEAD，不将阶段 DONE 等同于已安装验收。

---

# 25. 每 Phase 执行纪律

每 Phase：

实现  
→ 测试  
→ 更新 task  
→ commit  
→ push  
→ 确认远程  
→ 下一 Phase。

---

# 26. 最终验证

至少：

```bash
dotnet restore
dotnet build -c Release
dotnet test -c Release
```

确认：

- Engine tests 不回归；
- UI tests 通过；
- App 正常启动；
- Windows CI PASS；
- macOS core CI PASS。

---

# 27. 最终 Test Build

从最终 HEAD：

运行：

`Build Windows test installer`

下载并实际安装。

不得创建：

- Tag
- Release

---

# 28. Git

建议 Phase commits：

1. `feat: add quick comparison entry workflow`
2. `feat: add comparison execution experience`
3. `feat: add comparison results and change list`
4. `feat: add comparison filtering and review states`
5. `feat: add comparison document preview`

实际 message 按实现调整。

禁止：

- force push
- reset --hard
- amend 已推送 commit
- Tag
- Release

---

# 29. 最终报告

## Comparison UI v0

### Quick Compare

- File picker：
- Drag/drop：
- Baseline/current：
- Validation：

### Execution

- Progress：
- Cancellation：
- Errors：
- Partial parsing：
- Persistence：

### Results

- Summary：
- Change list：
- Grouped：
- Individual：
- Text diff：
- Format diff：
- Move：
- Comments / Revisions：

### Review

- 未处理：
- 已确认：
- 忽略：
- Group sync：
- Persistence：

### Filters / Search

- Status：
- Type：
- Search：
- Count：

### Preview

- Baseline：
- Current：
- Tables：
- Highlight：
- Change navigation：
- Linked scroll：
- Unlink：
- Virtualization：

### Performance

- Small：
- Medium：
- UI responsiveness：

### Tests

- Total：
- Passed：
- Failed：

### Platform

- Windows：
- macOS CI：

### Packaging

- Test Build version：
- Commit SHA：
- Run：
- Setup installed：
- Portable：

### Manual Trial

- Compare：
- Search：
- Filter：
- Review state restart：
- Uninstall：

### Documentation

- comparison-ui.md：
- task：

### Git

- Phase commits：
- Push：
- Working tree：

### Known Limitations

明确列出下一阶段要解决的产品能力。

最终判断：

> Comparison UI v0 是否已经具备供用户使用真实非敏感合同进行初步 Final Check 产品试用的条件。
