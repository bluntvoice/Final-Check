# Final Check — Project / Template / Version Management v0

> Status: IN PROGRESS
> Version: v0.1.0 development
> Scope: Template Center / Contract Project / Version & Round Management / Comparison History
> Depends on: Document Engine v0, Comparison Engine v0, Format Restore Engine v0, Storage Foundation v0, Comparison UI v0
> Last updated: 2026-09-13

---

## 1. 任务目标

将现有 Quick Compare 升级为 Final Check 正式的合同版本管理工作流。

本阶段完成后，用户应能够：

```text
维护标准模板
    ↓
创建合同项目
    ↓
绑定模板
    ↓
导入我方 / 对方版本
    ↓
指定谈判轮次
    ↓
设置当前我方基准版本
    ↓
选择比对基准
    ↓
生成独立 Comparison
    ↓
查看历史版本 / 历史比对
```

现有 Quick Compare 保留，不删除。

---

# 2. 产品核心模型

需要正式建立四个用户可见概念：

1. Template
2. Contract Project
3. Contract Version
4. Negotiation Round

以及已经存在的：

5. Comparison Record

---

# 3. 开始前必须重新读取

执行前重新读取：

* `AGENTS.md`
* 本 task
* `docs/PRD/PRD-v0.1.0.md`
* Comparison UI task / architecture
* Document Engine architecture
* Comparison Engine architecture
* Format Restore architecture
* Storage architecture
* 当前 Data / App / Desktop / ViewModel / Navigation / Persistence 实现

重点核对：

* 当前 Quick Compare persistence model
* ComparisonResult / Snapshot identity
* Review state persistence
* DataRoot 路径
* 当前数据库 schema / migrations

不得根据聊天记忆假设字段存在。

---

# 4. 本阶段不做

本轮暂不实现：

* 自动 Top 3 模板匹配
* AI
* Format Restore 正式 UI
* Excel/TXT/Markdown 正式导出 UI
* Backup/Restore UI
* Storage Settings UI
* Installer Wizard
* 软件内更新
* Stable Release

允许为后续功能预留字段和接口，但不要扩大 scope。

---

# 5. Review 状态文案修正

在本阶段首先同步 Comparison UI 文案：

当前：

* 未处理
* 已确认
* 忽略

统一修改为：

* 未处理
* 已审阅
* 忽略

其中：

> “已审阅”只表示用户已经人工看过该差异，不表示接受、同意或采纳该合同修改。

如果底层目前使用：

* `Confirmed`
* `IsConfirmed`
* 数据库 confirmed 状态

可以继续保留内部枚举/字段。

本阶段原则上只修改：

* 中文 UI 文案
* Tooltip
* 空状态 / Filter 文案
* 用户说明

避免仅因展示语义进行不必要的数据迁移。

后续独立设计：

`处理意见`

例如：

* 待决定
* 接受
* 拒绝
* 需沟通
* 我方已修改

本阶段不实现。

---

# 6. Phase 1 — Template Center v0

建立第一版模板中心。

## 6.1 Template 实体

至少包含：

* TemplateId
* Name
* ContractType
* Version
* FilePath
* FileName
* FileSize
* ModifiedAt
* SHA256
* SnapshotId
* IsCurrent
* IsEnabled
* CreatedAt
* UpdatedAt
* Notes（可选）

模板原始 DOCX 仍采用：

> 路径 + metadata + hash

不要复制原文件到 DataRoot。

Snapshot 正常持久化。

---

## 6.2 模板版本

同一个逻辑模板允许多个历史版本。

例如：

```text
货运代理协议
├─ v1.0
├─ v1.1
└─ v1.2  当前
```

新增模板版本：

* 保留旧版本；
* 建议下一语义版本；
* 用户可以修改；
* 同一逻辑模板只允许一个 Current Version。

不要因为设新版本为 Current 删除旧版本。

---

## 6.3 Template Center UI

页面至少支持：

* 模板列表
* 新增模板
* 新增版本
* 查看历史版本
* 设置当前版本
* 启用 / 停用
* 修改名称 / 合同类型
* 查看源文件路径
* 重新关联丢失文件

---

## 6.4 删除

删除模板前必须检查：

* 是否被项目绑定；
* 是否被历史 Comparison 引用。

历史 Comparison 已持久化 Snapshot，不得因删除模板而破坏已有比对结果。

如果有引用：

明确警告。

---

## 6.5 源文件失效

如果模板源 DOCX：

* 被移动
* 被重命名
* 被删除

优先使用现有文件重关联机制。

Snapshot 仍保留，可查看历史，但不能假装可以重新生成原始 DOCX。

---

## Phase 1 验收

至少：

* 创建模板
* 多版本
* Current 切换
* 禁用
* 删除警告
* 路径失效
* 重关联
* Snapshot persistence

完成后：

`Phase 1 — DONE`

建议 commit：

`feat: add template center and version management`

---

# 7. Phase 2 — Contract Project v0

建立合同项目。

## 7.1 Project 字段

至少：

* ProjectId
* ProjectName
* Counterparty
* ContractType
* BoundTemplateId
* BoundTemplateVersionId
* FolderId（预留一层）
* Tags
* Status
* CreatedAt
* UpdatedAt
* CurrentBaselineVersionId
* Notes

---

## 7.2 Project Name

创建项目时允许用户填写。

同时提供建议值：

> 合同标题 + 对方名称 + 文件名信息

如果当前阶段无法稳定抽取合同标题/对方：

允许只做轻量建议，不要为了这项功能新建复杂 NLP。

用户最终确认。

---

## 7.3 Counterparty

优先使用已有文档内容可可靠提取的信息。

无法可靠判断：

留空让用户填写。

不要猜。

---

## 7.4 Contract Type

如果项目绑定模板：

默认继承模板 ContractType。

用户可以手动修改项目 ContractType。

修改 ContractType：

> 不自动切换模板。

---

## 7.5 项目列表

至少显示：

* 项目名称
* 对方
* 合同类型
* 当前模板
* 当前基准版本
* 最近更新时间

默认：

> UpdatedAt descending

---

## 7.6 Filters

至少：

* 项目名称搜索
* 模板
* 合同类型
* 更新时间

Archive 后续 Phase 实现。

---

## Phase 2 验收

至少：

* 创建
* 编辑
* 绑定模板
* 解绑 / 切换模板
* 列表
* Search
* Filter
* Persistence

完成：

`Phase 2 — DONE`

建议：

`feat: add contract project management`

---

# 8. Phase 3 — Version Import / Role / Negotiation Round

这是本阶段核心。

## 8.1 Version 实体

至少：

* ContractVersionId
* ProjectId
* FilePath
* FileName
* FileSize
* ModifiedAt
* SHA256
* SnapshotId
* Role
* RoundNumber
* IsCurrentBaseline
* ImportedAt
* Notes
* OriginalSourceMetadata

---

## 8.2 Role

每个版本必须由用户明确选择：

* 我方版本
* 对方版本

严禁根据：

* 文件名
* 修改人
* 路径
* Word metadata

自动推断角色。

可以记住上次选择，但必须由用户确认。

---

## 8.3 Negotiation Round

导入时用户选择：

* 当前轮
* 下一轮
* 指定已有轮次

同一轮：

允许多个我方版本；
允许多个对方版本。

---

## 8.4 Round UI

项目详情页默认按轮次显示：

```text
第 1 轮
  我方 v1
  对方 v1

第 2 轮
  我方 v2
  我方 v2.1
  对方 v2

第 3 轮
  对方 v3
```

同时允许切换：

> 按时间顺序

---

## 8.5 Batch Import

支持：

* 文件选择器多选
* Drag & Drop 多文件

导入前显示列表。

每个文件可设置：

* 我方 / 对方
* 轮次
* 删除
* 排序

批量导入：

> 只保存文件 metadata / hash / Snapshot / Version。

不要自动触发 Comparison。

---

## 8.6 左右拖拽区域

如果实现成本合理：

项目版本导入 Dialog 可提供：

左侧：

> 我方版本

右侧：

> 对方版本

两个区域均允许多文件。

如果当前 Avalonia Drag/drop 结构不适合，不要做脆弱实现；至少保证角色明确。

---

## 8.7 Duplicate

SHA256 完全相同：

提示：

> 已存在内容完全相同的版本。

允许：

* 跳过
* 仍然导入

仍导入时：

标记 SameContent / DuplicateReference。

---

## Phase 3 验收

至少：

* 单文件
* 多文件
* 我方 / 对方
* 当前轮
* 下一轮
* 指定轮次
* Duplicate
* Drag/drop
* Persistence
* 原 DOCX hash 不变化

完成：

`Phase 3 — DONE`

建议：

`feat: add contract version and negotiation round management`

---

# 9. Phase 4 — Current Own Baseline / Comparison Workflow

正式建立 Final Check 的谈判比对逻辑。

## 9.1 Current Baseline

只有：

> 我方版本

可以设置为：

`当前基准版本`

对方版本不能成为 Current Own Baseline。

---

## 9.2 新我方版本

导入新的我方版本后：

询问：

> 是否设为当前基准版本？

默认：

> 否。

不要自动覆盖已有基准。

---

## 9.3 比对基准

从任意项目版本发起 Comparison 时：

用户明确选择：

### A. 标准模板

使用项目绑定模板的当前版本，或手动选择历史模板版本。

### B. 当前我方基准版本

使用项目当前 Own Baseline。

---

## 9.4 默认选择

记住该项目上一次使用的 baseline type。

下次预选。

但：

> 不自动开始 Comparison。

用户仍需确认。

---

## 9.5 对方版本的典型行为

例如：

```text
我方 v1
↓
对方 v1
↓
我方 v2  ← 当前基准
↓
对方 v2
```

对方 v2 应默认建议与：

> 我方 v2

比较。

不是与：

> 对方 v1

比较。

这是硬性业务规则。

---

## 9.6 独立 Comparison

每次 Comparison 都创建新的独立记录。

同一个 Version：

允许有：

* 与模板 v1.0 比较
* 与模板 v1.1 比较
* 与我方 v2 比较

历史记录不得覆盖。

---

## 9.7 Template Switch

项目切换模板：

只影响未来 Comparison 默认建议。

历史 Comparison 保持原基准 Snapshot。

---

## 9.8 Comparison UI 复用

调用已有 Comparison UI。

不要新写第二套结果页。

项目模式只是增加：

* 上下文
* baseline selection
* history linkage

---

## Phase 4 验收

至少：

* 设置我方 baseline
* 对方不能设 baseline
* 新我方版本询问
* Template baseline
* Own baseline
* 同一版本多 Comparison
* Template switch 不影响历史
* Review state persistence

完成：

`Phase 4 — DONE`

建议：

`feat: integrate project comparison baselines`

---

# 10. Phase 5 — Project Detail / History / Archive

建立第一版项目工作台。

## 10.1 Project Detail

至少显示：

### 基本信息

* 项目名称
* 对方
* 合同类型
* 模板

### 当前状态

* 当前我方基准
* 最新版本
* 最新 Comparison
* 未处理变化数量
* 已审阅数量
* Pending Restore count（底层有则显示）

### Versions

* Round timeline

### Comparison History

* 历史比对列表

---

## 10.2 Version Detail

点击版本至少显示：

* 文件名
* 原路径
* hash 摘要
* 文件角色
* Round
* Baseline 状态
* 导入时间
* Snapshot 状态
* 相关 Comparison
* Restore Working Copy 状态（如存在）

---

## 10.3 Comparison History

至少显示：

* Comparison 时间
* 当前版本
* Baseline
* Baseline Type
* 总变化
* 未处理
* 已审阅
* 忽略

点击：

进入已有 Comparison Result UI。

---

## 10.4 历史不可变

历史 Comparison 的：

* baseline identity
* snapshot identity
* changes

不得因为：

* 模板更新
* Current baseline 改变
* 项目字段修改

而重算或覆盖。

---

## 10.5 Archive

项目支持：

> 归档

归档后：

* 不出现在默认活跃项目列表
* 数据保留
* 可以恢复

参考 In Line 的 Archive 思路。

---

## 10.6 Delete / Recycle

删除项目：

进入软件回收站。

不要直接永久删除。

本阶段至少建立：

* Recycle state
* 恢复

如果完整“回收站页面”实现量过大，可做基础页面。

永久删除仍需显式二次确认。

---

## 10.7 Permanent Delete

永久删除：

删除：

* 项目 metadata
* versions
* managed snapshots
* comparisons
* review states
* restore records

但：

> 不删除外部用户原始 DOCX。

---

## Phase 5 验收

至少：

* project detail
* round timeline
* version detail
* comparison history
* open history
* archive
* restore archive
* recycle
* restore recycle
* permanent delete safety

完成：

`Phase 5 — DONE`

建议：

`feat: add project timeline and comparison history`

---

# 11. Folder / Tag 基础

如果项目模型当前适合，本阶段加入第一版：

## Folder

仅：

> 一层目录

例如：

```text
美国客户
供应商
进行中
```

不做多级目录。

---

## Tags

项目支持多个 Tag。

Tag：

* Name
* Color

支持：

* 自定义颜色
* 预设颜色

后续多层 Folder 可以再设计。

---

# 12. Navigation 建议

正式导航可逐步形成：

```text
首页
快速比对
合同项目
模板中心
归档 / 回收站
关于
```

不要把“Storage Settings”等尚未实现页面做假入口。

---

# 13. Quick Compare 保留

现有 Quick Compare：

继续可用。

不要要求所有临时比对都创建 Project。

Quick Compare 与 Project Comparison 共用：

* Document Engine
* Comparison Engine
* Result UI
* Review UI

不要维护两套实现。

---

# 14. Quick Compare → Project

如果实现成本合理：

允许未来将某次 Quick Compare：

> 保存为项目

本阶段不是硬要求。

不要为此阻塞主要任务。

---

# 15. 数据模型与 Migration

新增数据库实体时：

必须建立 EF migration。

确保现有用户：

* Comparison history
* Snapshots
* Review state
* Format Restore
* Storage

全部保留。

不得重建数据库。

---

# 16. Source File Path

所有模板 / 版本原始 DOCX：

只记录：

* path
* filename
* size
* mtime
* hash

不得复制到 DataRoot。

---

# 17. Path Relink

源文件不存在：

使用已有路径恢复策略：

1. 原目录附近
2. filename
3. size
4. mtime
5. hash

高可信时提示候选。

否则：

手动重新关联。

---

# 18. Same Hash / Changed Hash

同 hash 不同路径：

提示：

> 可能是同一文件移动或重命名。

允许更新路径。

同路径但 hash 已改变：

提示：

> 文件内容已发生变化。

允许：

* 作为新版本导入
* 保留旧版本记录

不得覆盖历史 Snapshot。

---

# 19. Review 文案统一

所有 UI 中：

`已确认`

统一替换为：

`已审阅`

包括：

* Filter
* Group status
* Summary
* Project detail counts
* Comparison history
* Tooltips

内部 `Confirmed` 可保留。

测试必须覆盖：

> persisted Confirmed state 仍显示为 已审阅。

---

# 20. 性能

至少验证：

* 100 Projects
* 单项目 50 Versions
* 单项目 30 Comparisons
* 20 Templates / versions

列表不能明显卡顿。

使用分页 / virtualization / lazy load。

不要启动项目页时：

> 一次读取所有 Snapshot JSON。

---

# 21. UI Thread

下列操作后台：

* hash
* snapshot parse
* version import
* comparison
* source relink scan

UI 保持响应。

---

# 22. Empty States

完善：

### 无模板

> 尚未添加标准模板。

### 无项目

> 尚未创建合同项目。

### 无版本

> 导入第一份合同版本开始管理。

### 无历史 Comparison

> 尚未进行版本比对。

不要空白页。

---

# 23. Tests

继续保证现有：

240 tests

不回归。

新增：

* Template service tests
* Project service tests
* Version import tests
* Round tests
* Baseline tests
* Comparison history tests
* Archive/recycle tests
* UI ViewModel tests
* EF migration tests

---

# 24. Real-world Manual Trial

全部完成后：

从最终 HEAD 生成最新 Test Build。

实际安装。

使用至少一组脱敏合同模拟：

```text
Template v1
↓
我方 v1
↓
对方 v1
↓
我方 v2
↓
设我方 v2 为 Current Baseline
↓
对方 v2
↓
对方 v2 vs 我方 v2
↓
查看历史
```

再测试：

* 模板升级
* 历史 Comparison 不变化
* 重启恢复
* Archive / Restore
* Recycle / Restore

---

# 25. Drag/drop 实际试用

上一阶段最终安装包尚未实际用 Drag/drop 完成试用。

本阶段最终人工验收时：

必须实际拖入：

* Template DOCX
* Contract Version DOCX

至少各一次。

记录结果。

---

# 26. Documentation

新增：

`docs/architecture/project-template-version-management.md`

至少说明：

* Template model
* Project model
* Version model
* Negotiation Round
* Own/Counterparty role
* Current Own Baseline
* Comparison baseline
* History immutability
* File relink
* Archive/recycle

更新：

* PRD
* architecture README
* development README
* task

---

# 27. Phase 状态

* Phase 1 — Template Center：DONE
  * 实际完成（2026-09-13）：逻辑模板 / 历史版本模型、模板 DOCX picker/drop adapter、当前版本唯一切换、名称/类型/启停编辑、手动及附近 hash 候选重关联、冻结快照原生预览、删除引用警告与独立确认（逻辑删除，保留历史）。导入后台只读解析 / hash 前后验证，短 data scope + SQLite transaction 追加 Snapshot/version；源文件不复制进 DataRoot。
  * 数据兼容：schema 5 增量 Templates / TemplateVersions、Restrict FK 与唯一索引，旧 Snapshot / Comparison / review payload 保留；readonly bootstrap inspector、Storage migration digest / identity 校验及原文排除同步纳入模板。
  * 已审阅语义：UI 状态/筛选/组汇总/动作/Tooltip 与 PRD 改为已审阅；仅表示人工查看，保留稳定 Confirmed 和原 payload schema，不为文案迁移数据库。
  * 验证：新增 6 项真实模板/旧 schema 升级/Storage migration 测试、4 项 VM/已有 Confirmed 展示兼容测试；Release 全量 250/250、Desktop build 0 warning / 0 error。项目引用检查将在 Phase 2/4 随实际项目/比对关联扩展；最终安装包实际 Template/Version Drag & Drop 与版本链验收仍待 Phase 5 完成后从最终 HEAD 执行，不以 adapter/VM 测试冒充实际拖拽验收。
* Phase 2 — Contract Project：DONE
  * 实际完成（2026-09-13）：项目创建/编辑、明确填写对方、模板绑定/切换/解绑、空类型继承与手动覆盖、一层逻辑 Folder、自定义/预设颜色 Tag；项目元数据列表按更新时间倒序，支持名称/模板/类型/时间/Folder/Tag 筛选、20 行分页和虚拟化。过滤或刷新后保留编辑项目和模板版本身份，避免误创建或静默解绑。
  * 数据兼容：schema 6 增量 Projects / ProjectFolders，模板版本归属校验，停用模板保留既有绑定但拒绝新绑定；Storage inspector / migration facts 同步项目及标签，旧快照/历史 payload 不变；模板删除提示纳入项目引用。
  * 验证：新增 5 项 Data 和 4 项 App 测试，Release 全量 259/259、Desktop build 0 warning / 0 error；100 项目首个 20 行元数据页冷查询 534.4 ms（包含 EF 冷启动，后台执行），损坏 Snapshot JSON 不影响元数据列表。Phase 3–5 尚未开始，最终安装及实际拖拽仍待最终 HEAD 验收。
* Phase 3 — Version / Round：DONE
  * 实际完成（2026-09-13）：后台批量只读导入、逐文件明确角色/轮次的多选 picker/drop 队列、排序/移除、相同 hash 默认跳过与显式重复引用；轮次与版本增量 schema 7、元数据分页/按轮次或时间排序、共享原生冻结快照预览、同 hash 源路径重关联与原始来源保留。当前轮/下一轮/已有轮次可选择，不跳过轮次；导入既不自动比对，也不改变基准。
  * 安全与兼容：一个事务追加完整批次，部分失败/取消回滚；加入队列后 hash 改变要求重新加入确认；旧 schema 6 payload 增量升级保留。Storage facts / original exclusions 纳入轮次、版本及原始来源；源 DOCX 位于旧 DataRoot 内时也不迁移或删除。
  * 验证：新增 5 项 Data / 3 项 VM 测试，Release 全量 267/267、Desktop build 0 warning / 0 error，版本选择修正后 App 41/41 复测。50 版本/20 模板元数据页 26.4 ms，不读取 Snapshot JSON。实际 Template/Version 系统 Drag & Drop 待最终 HEAD 安装包验收；左右角色拖拽区域非硬要求未实现，角色逐行明确确认。
* Phase 4 — Baseline / Comparison：DONE
  * 实际完成（2026-09-13）：本项目 Own-only 基准校验、新我方导入独立默认否提示、模板当前/历史或当前我方基准选择、项目 baseline type 记忆与对方版本当前我方建议；只预选、不自动执行，也不使用上一版对方作基准。冻结 Snapshot 调用现有 Engine，共享结果/审阅页与历史打开，支持后台执行及取消/离开提示。
  * 历史安全：schema 8 原子追加项目 Comparison context 与既有独立冻结 Record/result，保存前再次校验归属与输入身份；模板/基准切换不重算旧记录。模板删除引用包含历史 context，即使项目已切换模板；元数据分页从稳定 review payload 读取实际计数，不维护第二套审阅状态。
  * 验证：新增 5 项 Data / 3 项 VM 测试，Release 全量 275/275、Desktop build 0 warning / 0 error；包含同版本多 Comparison、Own-only、模板切换、Confirmed 保留、原文失效、取消、schema 7 增量升级和 Storage 完整迁移。30 历史/20 行元数据页 71.0 ms，不读取 Snapshot/result JSON。
* Phase 5 — Project Detail / History / Archive：TODO

---

# 28. 每 Phase 纪律

每 Phase：

实现
→ 测试
→ task update
→ commit
→ push
→ confirm remote
→ next Phase

---

# 29. 最终验证

至少：

```bash
dotnet restore
dotnet build -c Release
dotnet test -c Release
```

并确认：

* Windows CI PASS
* macOS core CI PASS
* Desktop launch PASS
* existing Comparison workflow PASS

---

# 30. Test Build

从最终 HEAD：

运行 Windows Test Build。

报告必须明确：

* Version
* Commit SHA
* Run ID

实际：

* 安装
* 启动
* Template
* Project
* Version import
* Comparison
* Restart
* History
* Archive
* Uninstall

不得创建 Tag / Release。

---

# 31. Git

建议：

### Phase 1

`feat: add template center and version management`

### Phase 2

`feat: add contract project management`

### Phase 3

`feat: add contract version and negotiation round management`

### Phase 4

`feat: integrate project comparison baselines`

### Phase 5

`feat: add project timeline and comparison history`

禁止：

* force push
* reset --hard
* 已推送 commit amend
* Tag
* Release

---

# 32. 最终报告

## Project / Template / Version Management v0

### Template Center

* Create：
* Version：
* Current：
* Enable/disable：
* Source relink：
* Delete safety：

### Projects

* Create/edit：
* Counterparty：
* Contract type：
* Template binding：
* Search/filter：
* Folder/tag：

### Versions

* Own：
* Counterparty：
* Round：
* Batch import：
* Drag/drop：
* Duplicate：

### Baseline

* Current own baseline：
* Validation：
* New own version prompt：

### Comparison

* Template baseline：
* Own baseline：
* Default baseline memory：
* Multiple history：
* Template switch：

### Review terminology

* 未处理：
* 已审阅：
* 忽略：
* Internal persisted compatibility：

### Project Detail

* Timeline：
* Version details：
* Comparison history：
* Review counts：

### Archive / Recycle

* Archive：
* Restore：
* Recycle：
* Permanent delete：
* External DOCX safety：

### Compatibility

* Existing Quick Compare：
* Existing Comparison history：
* Snapshot：
* Format Restore：
* Storage migration：

### Tests

* Total：
* Passed：
* Failed：

### Performance

* Projects：
* Versions：
* Comparison history：

### Platform

* Windows：
* macOS：

### Packaging

* Test version：
* Commit：
* Run：

### Manual Trial

* Template：
* Project：
* Version chain：
* Baseline：
* History：
* Drag/drop：
* Restart：
* Uninstall：

### Git

* Commits：
* Push：
* Working tree：

### Known Limitations

明确列出下一阶段功能。

最终判断：

> Project / Template / Version Management v0 是否已经具备让 Final Check 从“单次比对工具”进入“合同版本管理工具”阶段的稳定基础。
