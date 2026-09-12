# Comparison UI v0

## 边界与入口

Quick Compare 是不要求项目、模板或版本链的独立入口：首页 → 新建比对 → 基准版本 / 当前版本。后续项目模式可复用该工作流，不提前建立完整项目 UI。

## Session 与输入适配

`ComparisonSession` 在 App/ViewModel 层保存会话 ID、两侧外部文件身份、解析/执行状态、结果 ID、时间、错误与诊断。`ComparisonSetupViewModel` 管理选择、替换、移除与按钮可用性；View code-behind 仅适配 Avalonia StorageProvider 和 DragDrop，不解析 DOCX、不访问 SQLite。

`IComparisonFileInspector` 的 Infrastructure 实现在后台只读校验后缀、路径、权限和 SHA-256，并返回名称、绝对路径、大小与修改时间。原始文件不复制到 DataRoot、不设置只读、不改写。每侧一次一个 DOCX；无效选择不替换已有有效输入。执行期间输入锁定，避免会话角色变化。

## 实施状态

Phase 1 为入口与文件会话基础；完整工作流服务、历史、结果管理与预览将在 Phase 2–5 按 task 逐阶段接入。入口基础不代表完整比对已可试用，真实状态见 [task](../tasks/comparison-ui-v0.md)。

## 工作流服务与执行 UX

`IComparisonWorkflowService` 在 Core 定义，Desktop 的 Application orchestration 组合现有 parser、engine 和短生命周期数据 scope；App 不引用 Documents、Comparison 实现或 EF。后台重新校验两侧 hash / metadata；相同路径/内容先由 VM 请求用户继续或取消。执行持有只读源流，解析 hash 与选定身份一致且结束前再次校验，再保存结果。文件变化明确拒绝重试；已保存历史只从冻结 Snapshot 加载，不再解析源文件。

引擎阶段直接转换为中文提示，使用不定进度条，不伪造百分比。取消向实际 validation / parser / engine / persistence 传递 token，提交临界区前可回滚，已完成提交不冒充取消。重复执行受 VM busy / command gate 阻断；导航离开执行页需要明确取消决定。Partial 保留解析 code / 位置摘要，先提示可能不完整，用户选择继续；错误按文件缺失/权限/加密/无效包/取消/意外分别表达，技术详情只显示异常种类或错误 code，不泄露正文和 stack。

## 独立历史与存储

## 结果组织与文字高亮

`ComparisonResultsViewModel` 消费冻结的 `ComparisonWorkflowResult`；每个 `ChangeItemViewModel` 引用原领域 ChangeItem，`ChangeListEntry` 仅组织 Engine Group ID / members，不重新归并或生成第二套变化。默认归并，逐项模式与组成员位置选择共用同一 item VM。概要复用统计；详情展示原文/现文、多格式属性、两侧位置、Word 修订/批注、低可信与必要诊断。位置用 Snapshot 的正文/表格/行/列节点索引；没有精确页码能力就不伪造页码。

`DifferenceHighlight` 按 Engine span 的 UTF-16 start/length 投影连续文本片段；`DiffTextBlock` 仅渲染局部背景色。全文仍完整保存，段落插入/删除可突出整个真实新增/删除段落；移动无文字变化不把正文标成文字修改。首页“继续最近一次比对”通过工作流后台加载冻结历史，原文件删除/移动不影响查看。

Database schema 4 的增量 `ComparisonRecords` 引用两份 DocumentSnapshots 和一个 ComparisonResults（FK Restrict），payload schema 1 保存独立 RecordId、外部路径/metadata/hash、时间和原 ChangeId → review state。不预建项目实体，后续可另行关联；每次比对追加，不覆盖历史。快照/结果/record 单一 SQLite transaction 写入，任一步失败/提交前取消都不能留下半条记录。读取核对外键、hash、snapshot identity 和 review keys；未知 schema 拒绝。

数据 scope 只在一次存取操作内使用，结束即释放共同 storage lease，不在 ViewModel 中持有 DbContext。新表加入 readonly bootstrap inspector、迁移 logical facts / payload digest / 引用验证与占用计算；两侧原始路径加入排除清单，即使原文恰好位于旧 DataRoot 也不迁移。记录/review payload 是包含于 Database 的 Comparison 逻辑 bytes，不能重复加到物理总计。旧 schema 3 的原 payload 在增量升级中保持原样。
