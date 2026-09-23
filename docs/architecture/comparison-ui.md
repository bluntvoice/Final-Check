# Comparison UI v0

## 边界与入口

当前已实现的 Quick Compare 是不要求预建项目、模板或版本链的入口：首页 → 新建比对 → 基准版本 / 当前版本；项目模式已复用同一 Engine / 结果 / 审阅 UI。以下实现描述保留 v0 阶段事实，不代表已满足全部校准后产品目标。

## PRD 校准后的目标（Stage A 进行中）

以 [当前 PRD](../PRD/PRD-v0.1.0.md) 第 5、9–12、23–30、64 章为准：直接提供文件，正式成功后自动项目化；版本自动编号，轮次按需展开，普通比较可暂未指定角色；有绑定模板时默认其当前启用版本，无绑定时区分唯一高可信推荐 / Top 3 / 手动选择。自动匹配只预选，仍须点击开始。

Application workflow 负责可恢复/事务性保存项目、参与版本、Snapshot、独立 Comparison 与上下文，不由 View 拼装数据库写入；复用现有 frozen record 与 history，失败/取消不能生成空项目或假成功。Stage A2 已将角色未知建模为独立 `Unspecified` 值，而非通过 UI 隐藏必填或映射为 Counterparty；增量 schema 10、自动版本号和事务边界详见 [project-template-version-management.md](project-template-version-management.md)。

Comparison Workspace 使用统一 selected ChangeId / group member，将清单、两侧上下文、详情、批注、状态和前后项导航同时更新；按需展开的全文双栏复用同一选择。Stage A1 已将旧的两个页签合并为同屏工作台。Review 只是阅读进度，操作增加可撤销反馈，永久删除仍二次确认。

### Stage A3 template recommendation and execution

无项目 Quick Compare 选择当前 DOCX 后，`TemplateRecommendationService` 在后台解析该文件一次，并从启用模板的已保存 DocumentSnapshot 提取有界特征；不打开历史模板源 DOCX，不在 UI 中运行 Engine。特征覆盖文件名、元数据/可识别标题、条款标题、正文三字符片段、表格行列结构及段落数量；固定权重分别为 0.04 / 0.07 / 0.24 / 0.52 / 0.08 / 0.05，当前启用版本额外 +0.03。正文最多取前 200 非空段/2 万规范化字符与 3000 个片段，最多缓存 256 份模板版本特征；目录以 100 条元数据分页、快照延迟读取，取消令牌贯穿。模板解析/身份异常跳过并诊断，不以文件名独断。当前文档 Partial 不自动匹配。

得分低于 0.60 不列入候选；首项 ≥0.78 且比第二项高至少 0.08 才归为高可信唯一、直接预选。否则多个可信候选展示 Top 3（版本、当前/历史、分数）并预选首项；唯一但未达高可信阈值或无候选时不自动选定，要求手动基准。阈值是本地推荐信号，不是法律结论；文件/模板变化后可主动重新匹配。项目已有冻结版本也可直接以 Snapshot 重新匹配，不依赖已移走的原始 DOCX；此时可显式选择其他启用模板作为本次基准，但不会暗中修改项目逻辑模板绑定。即使已预选，用户仍必须点击“开始比对”，手动基准 DOCX 可覆盖推荐。

模板基准执行先重新解析所选当前文件并校验 SHA-256；模板侧直接载入不可变 Snapshot，原模板 DOCX 移走仍可比。Engine 不在 View 实现。保存事务一次写入独立 record/result/Snapshots、自动 Project、V1/V2、逻辑模板绑定和 TemplateVersion 历史链接；失败不留下空项目。成功历史记录实际 TemplateVersionId 和冻结快照，模板 current 后续变化不重算旧结果。显式选择的历史模板版本也可执行，但不会被误当作新的 current。项目内绑定模板默认规则详见 [project-template-version-management.md](project-template-version-management.md)。

### 默认 Context View 与按需 Full Document（Stage A 展示修正，自动测试已覆盖，安装包人工验收待执行）

默认 Workspace 由修改清单、当前 ChangeItem 的 Baseline/Current Context Panel、详情/批注/状态操作组成；原 A1 完整双栏 `ComparisonPreviewView` 保留为按需 Full Document Mode，不改写冻结 Snapshot、ComparisonResult 或 `LogicalScrollCoordinator`。模式是同一结果 VM 上的展示状态，切换时保持 selected ChangeId、筛选、review 和 Undo；进入全文时定位当前 item，返回 Context 时重建同一 item 的两侧上下文，不创建第二份比对结果。

Context Projection 从已投影的 `PreviewBlock`、Snapshot 节点索引及 Engine `ChangeItem` / DifferenceSpan / FormatDifference 构造，不重新解析 DOCX 或运行 Engine。当前冻结 Snapshot 的可预览结构是完整 Paragraph 与 Table Row / Cell，按目标块及必要前后块选择逻辑邻接；不按固定字符数裁剪。若后续引入可靠 Clause 边界，可在同一投影层扩大到整个条款，不从段落序号猜测条款。表格沿用行/单元格结构，文字高亮仅使用当前选中 ChangeItem 的 span；段落新增/删除及段落级格式差异可标记整段；纯移动标明来源/目标节点而不误标成文字变化。未匹配或低可信不填造另一侧文本。预建 node→block 索引并只重投影少量上下文块；100+ changes 连续切换不重新解析 DOCX。Full Mode 仍使用虚拟化列表和基于高可信 NodeMapping 的 Leader/Follower；Context Mode 无长距离联动依赖。

`ContextDocumentPanel` 以 `DocumentPanelId` 标识 Document Source，并携带 selected Change Mapping 的目标节点及不可变 preview blocks；当前双文件 VM 仍分别暴露 Baseline/Current 便于 XAML 绑定，但不把 Context 的数据契约写成两个全文控件。未来 Three-way 可扩展 source 与布局，本 Stage 不实现三文件结果或三栏。宽窗并列两侧上下文，普通宽度堆叠上下文、旁列详情，极窄窗口再把详情置于下方；全文模式在普通宽度为双栏让出空间。自动测试覆盖 selection/前后项/筛选审阅后的身份、高亮、段落/表格/移动上下文、Context ↔ Full、150 次连续切换和窄窗；Windows Debug CLI 的 368 项隔离夹具已走通往返及真实滚轮，最终 HEAD 安装包仍需人工连续审阅验收。

### Stage A1 工作台与状态撤销（已完成阶段验收）

当前 A1 将结果页改为同屏清单、两侧冻结 Snapshot 预览和详情/批注/审阅区；窄窗口把详情移到预览下方，仍无需切页。清单保留 Engine 原 Group/ChangeId，筛选及归并切换尽量保持当前成员选择；上一项/下一项沿当前可见修改顺序导航，不生成第二套 Diff。概览增加基于实际 review states 的未处理/已审阅/忽略计数，未来 Format Restore 可在详情状态动作区扩展，不在 A1 实现恢复按钮。

Review/Undo 只改 `ComparisonRecord.ReviewStates`，不改 Snapshot / 原始 ComparisonResult。每次成功操作记录每个 ChangeId 的原状态、目标状态（组内允许混合），Undo 在单条 SQLite payload compare-and-swap 中原子恢复这些原状态；先核对目标状态，若被其他窗口改动则拒绝撤销并提示重新加载，不能覆盖较新审阅。界面在数据库成功后才更新，并提供 inline 撤销反馈；Undo 完成后的最终状态随历史 record 持久化，重启可重读。Undo 栈是当前会话交互历史，不伪称跨重启可撤销所有旧操作。

滚动核心 `LogicalScrollCoordinator` 使用 `WorkspaceDocumentPanel` 列表及 NodeId link，核心不限定两栏；当前 UI 仅绑定 Baseline/Current 两栏。只有鼠标/滚动条/导航键显式输入可提升 Leader；Follower 的程序滚动不提升 Leader，也不会反向触发。视口实际 realized 元素中选最接近中心的 block 作为逻辑 anchor，33 ms 合并高频事件，anchor 未改变不重复定位。只使用高可信、分数 ≥0.8 且唯一的 Engine NodeMapping；附近最多两行无可靠链接就保持另一侧并提示，不用像素比例猜测。目标端先 `ScrollIntoView` 再只用本地像素把对应逻辑 block 尽量居中；跨文档仍以节点映射为唯一对应依据。解除联动保留各自位置，重开从当前 Leader 的最近 anchor 对齐。A1 已用 Debug 隔离不等长文档在真实窗口复核慢/快滚轮、滚动条、换侧及关闭/重开联动；触控板设备未测。后续仍须从最终 HEAD 安装 Test Build 做 Stage A 总验收。

Comparison Ignore Rules 使用独立的每次比对 policy，不复用人工 Ignored 状态；Stage A4 的只读投影策略如下。

### Stage A4 — 可逆的比对忽略规则

Quick Compare 与项目内比对均在“开始比对”前提供默认折叠的本次选项。选项由 Application workflow 传至保存事务，写入 `ComparisonRecord` payload schema 2 的 `IgnoreRules`；旧 schema 1 记录缺少此字段时按空规则读取，原有审阅状态仍以 ChangeId 独立保存。ComparisonResult schema 1 和完整 Snapshot 不改动。历史打开后读取当时的规则，工作台显示可见/原始总项，并可临时切换“显示完整差异”；该切换不写数据库、不改人工 Ignored/已审阅状态。结果清单、上下文与按需全文预览均使用同一只读投影，Format Restore 仍使用未投影的完整结果。

文字规则只对 Engine 原有 DifferenceSpan 作保守过滤：中英文列举标点及用户输入的逐字符 literal set 只有在忽略字符后旧/新 span 相同才隐藏；“忽略页码”只接受整段明确的 `第 N 页` / `Page N` / `N/M` 标记，“忽略序号”只接受段首明确编号且对应 span 落在编号内。正文金额、日期、天数或无可靠分类的 Word PAGE field 结果不猜测性隐藏。此策略不重新计算文字 diff；跨一个 span 混合的无法安全拆分差异会完整显示。用户可用“显示完整差异”核查法律含义，避免静默丢失事实。

格式规则按 `FormatDifferenceScope.Property` 过滤已稳定识别的字符、段落、表格及单元格属性；同一 ChangeItem 的未选属性和文字 span 继续展示。`GridSpan`、`VerticalMerge` 与 `TableStructureChanged` 属结构证据，即使“忽略全部格式”也不隐藏。当前引擎不输出字间距、表格行高和独立列宽差异，因此 UI 明示限制而不提供虚假开关；表格宽度仅指已解析的 table/cell 宽度。`ComparisonIgnoreProjection` 不持有数据库/引擎写入能力，不改原 ChangeId、Mapping、Snapshot 或 raw ComparisonResult。

Three-way Compare 是 Stage E 规划：复用三个 pairwise Comparison，通过共用模板/版本节点建立 Change Matrix 和三栏 Anchor，保留每个结果身份/可信度/未匹配诊断，不另造单一三文本 diff 或破坏双文件入口。Beta 前 Spike 评估，可明确延后 v0.1.x；本轮无模型/代码改动。Format Restore 正式 UI 属于 Stage C，优先在同一 Workspace 消费已有 Plan / Working Copy / Undo 服务。

## Session 与输入适配

`ComparisonSession` 在 App/ViewModel 层保存会话 ID、两侧外部文件身份、解析/执行状态、结果 ID、时间、错误与诊断。`ComparisonSetupViewModel` 管理选择、替换、移除与按钮可用性；View code-behind 仅适配 Avalonia StorageProvider 和 DragDrop，不解析 DOCX、不访问 SQLite。

`IComparisonFileInspector` 的 Infrastructure 实现在后台只读校验后缀、路径、权限和 SHA-256，并返回名称、绝对路径、大小与修改时间。原始文件不复制到 DataRoot、不设置只读、不改写。每侧一次一个 DOCX；无效选择不替换已有有效输入。执行期间输入锁定，避免会话角色变化。

## 实施状态

Phase 1–5 已实现入口、真实引擎工作流、独立历史、结果管理与双栏预览；自动化与开发窗口验证逐阶段记录于 [task](../tasks/comparison-ui-v0.md)。正式包可用性必须另以最终 HEAD 的 Windows Test Build 安装/重启/卸载试用验证，不能用源码窗口替代。

## 工作流服务与执行 UX

`IComparisonWorkflowService` 在 Core 定义，Desktop 的 Application orchestration 组合现有 parser、engine 和短生命周期数据 scope；App 不引用 Documents、Comparison 实现或 EF。后台重新校验两侧 hash / metadata；相同路径/内容先由 VM 请求用户继续或取消。执行持有只读源流，解析 hash 与选定身份一致且结束前再次校验，再保存结果。文件变化明确拒绝重试；已保存历史只从冻结 Snapshot 加载，不再解析源文件。

引擎阶段直接转换为中文提示，使用不定进度条，不伪造百分比。取消向实际 validation / parser / engine / persistence 传递 token，提交临界区前可回滚，已完成提交不冒充取消。重复执行受 VM busy / command gate 阻断；导航离开执行页需要明确取消决定。Partial 保留解析 code / 位置摘要，先提示可能不完整，用户选择继续；错误按文件缺失/权限/加密/无效包/取消/意外分别表达，技术详情只显示异常种类或错误 code，不泄露正文和 stack。

## 结果组织与文字高亮

`ComparisonResultsViewModel` 消费冻结的 `ComparisonWorkflowResult`；每个 `ChangeItemViewModel` 引用原领域 ChangeItem，`ChangeListEntry` 仅组织 Engine Group ID / members，不重新归并或生成第二套变化。默认归并，逐项模式与组成员位置选择共用同一 item VM。概要复用统计；详情展示原文/现文、多格式属性、两侧位置、Word 修订/批注、低可信与必要诊断。位置用 Snapshot 的正文/表格/行/列节点索引；没有精确页码能力就不伪造页码。

`DifferenceHighlight` 按 Engine span 的 UTF-16 start/length 投影连续文本片段；`DiffTextBlock` 仅渲染局部背景色。全文仍完整保存，段落插入/删除可突出整个真实新增/删除段落；移动无文字变化不把正文标成文字修改。首页“继续最近一次比对”通过工作流后台加载冻结历史，原文件删除/移动不影响查看。

## 独立历史与存储

Database schema 4 的增量 `ComparisonRecords` 引用两份 DocumentSnapshots 和一个 ComparisonResults（FK Restrict），payload schema 1 保存独立 RecordId、外部路径/metadata/hash、时间和原 ChangeId → review state。原 Quick Compare 不以预建项目为前提；Stage A2 在 schema 10 扩大保存事务，正式成功后自动创建项目、版本与历史关联。每次比对追加独立记录，不覆盖历史；任一步失败/提交前取消都不能留下半条记录或空项目。读取核对外键、hash、snapshot identity 和 review keys；未知 schema 拒绝。

数据 scope 只在一次存取操作内使用，结束即释放共同 storage lease，不在 ViewModel 中持有 DbContext。新表加入 readonly bootstrap inspector、迁移 logical facts / payload digest / 引用验证与占用计算；两侧原始路径加入排除清单，即使原文恰好位于旧 DataRoot 也不迁移。记录/review payload 是包含于 Database 的 Comparison 逻辑 bytes，不能重复加到物理总计。旧 schema 3 的原 payload 在增量升级中保持原样。

## 搜索、筛选与处理状态

状态按原 ChangeId 保存为未处理/已审阅/忽略。默认保留已审阅、隐藏忽略；状态筛选可显示忽略项并恢复，快捷入口仅看未处理。类型与即时搜索交集作用于原变化，搜索包含两侧全文、条款提示、位置与批注。计数是筛选后原变化数 / 全部原变化数，而不是归并组数。

归并组始终引用同一 item 状态，摘要根据全部成员计算（含筛选隐藏成员）；组操作明确只作用于当前显示成员，避免批量更改隐藏内容。数据库状态写入使用重新读取 + payload compare-and-swap，竞争时重新合并不同 ChangeId，不能把陈旧 VM 字典覆盖整条历史。只有保存成功后才更新 UI；失败保留原状态并提示重试。Snapshot 与 Comparison payload 永不随用户确认而改写。新 scope 加载冻结记录可恢复状态，原文件缺失也不影响恢复。

## 双栏预览与逻辑定位

`ComparisonPreviewBuilder` 在后台将冻结 Snapshot 投影为不可变 PreviewBlock / Cell / Paragraph / Segment；正文段落与表格按 SourceIndex 交错排序，表格按行虚拟化，不一次创建整个合同的复杂控件。保留 DisplayText 与 effective 基础字体/字号/粗体/斜体/颜色/下划线/删除线/高亮；与 Run 投影不一致时保留准确段落文字，跳过不可靠格式而非改写文本。Engine UTF-16 span 用于局部高亮，单段 Cell 的精确 span 可投影到该段；多段 Cell 的修改以单元格定位和完整详情表达，不猜测跨段文字范围。

原 Comparison UI v0 的结果右侧曾使用“文档对照 / 修改详情・审阅”两个页签；Stage A1 改为同屏工作台。双侧原生 ListBox 仍使用 VirtualizingStackPanel；清单、预览和归并成员选择均有有界视口与虚拟化。搜索/筛选一次替换清单集合，避免逐项通知引发反复布局。主窗口使用弹性比例、可换行筛选和滚动详情，最大化/还原保持导航。

点击原 ChangeItem 通过 node→block 索引定位段落、Run 或 Table/Row/Cell，并突出目标单元格/段落；找不到位置明确提示。`ComparisonPreviewViewModel` 复用 Engine NodeMappings 建立双向导航引用，不重新匹配。滚动适配器从实际可见 realized row 获取逻辑节点，另一侧将映射节点带入视口；不是像素级顶边对齐或 offset/extent 比例同步。解除联动两侧独立，重新开启按最后活动侧附近最多两行的可靠映射重新定位；Low confidence 不参与自动滚动猜测，无可靠映射另一侧保持不动并提示。显式变化定位的 Medium/Low 匹配保留人工确认提示。

预览是内容/上下文查看器，不是 Word 排版器；没有 Word 页码、高保真分页、浮动对象或复杂合并表格渲染保证。当前显示文本不恢复删除修订文字，修订和批注仍在详情保留完整证据。App 无 WebView / Chromium / Word COM，也不重用 HTML preview 引入浏览器 Runtime。异步结果模型生成完成后才在 UI 发布；导航在等待期间变化则不强制跳回结果页。
