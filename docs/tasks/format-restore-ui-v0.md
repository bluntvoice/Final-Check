# Final Check — Format Restore UI v0

> Status: IN PROGRESS
> Stage: C
> Version: v0.1.0 development
> Started: 2026-10-05
> Baseline: `47d47a9`

## 授权与范围

用户于 2026-10-05 明确选择进入 Stage C，保留 Stage B 安装器待验项。Stage A 人工触控板验收、B2 隔离安装/连续升级/卸载、B3 发布接入仍按各自任务记录，不因进入本阶段标记通过。

以 PRD 第 31–40、47.3–50、63.12、64 章为准。复用现有 Planner、Renderer、WorkingCopy journal 与 Undo 管线，保持原始 DOCX、Comparison 历史及修订/批注；不新增样式库或降低映射阈值，不更换技术栈、数据库 schema 或 packaging workflow，不创建 Tag/Release。

## Phase 与检查点

1. **C1 — 恢复计划与工作台预览：DONE**。从已保存比对解析真实 ContractVersion 身份；核验原文件或当前 Working Copy 后生成计划；工作台显示位置、当前/目标属性、类型、可信度、诊断和全部/类型/单项选择。分析只读，部分解析、外部修改、待恢复 journal 明确阻断。真实引擎/数据库、Headless 与隔离 Debug GUI 验证已通过，形成正常 commit/push 检查点。
2. **C2 — 确认执行与 Working Copy：PENDING**。明确清单确认、服务端重新核验输入/范围、真实阶段进度/取消、成功标记与即时重新分析；跨进程屏障及离开/关闭保护；保留 Comparison 原始事实。
3. **C3 — Undo / 外部编辑 / DOCX 导出：PENDING**。最近一次安全 Undo、外部修改显式保留/重新生成、独立 DOCX 默认路径与覆盖/自动改名/取消，失败恢复与重启验证。
4. **C4 — 交付验收：PENDING**。最终 HEAD 的 Windows/macOS CI、内部 Test Artifact 与隔离生成数据的实际 GUI/文件/历史/重启验证；样式/表格低可信边界如实记录，安装器门禁仍独立。

每个 Phase 完成实现、相关测试、任务/架构/开发说明更新，正常 commit/push 并核实远端后，才进入下一 Phase。现场证据保存在忽略的 artifacts；不将 Headless/单元测试等同于真实 GUI 或安装器验收。

## 当前证据

- 进入阶段前 main / origin/main 对齐，B1 最终实现 HEAD 的 CI/Test Build 与本机安装证据已核对并补齐到 Stage B task。
- C1 Release build：0 warning / 0 error；相关全量测试 356/356 PASS（Core 5、Documents 45、Comparison 52、Data 156、App 98）。初次 Data 新用例的测试 fixture 未接入 managed session、错误假定初始 Snapshot 数量，已改为与生产一致的 coordinator/session 和比较实际初始数量；修正后 Data 全量 156/156。最终布局调整后 App 全量 98/98，包含最小窗口 840×520 下虚拟化列表、当前/目标属性、返回审阅和安全选择。
- 最终整库 Release build 0 warning / 0 error；`test-version.ps1`、`test-release-infrastructure.ps1` 与 `git diff --check` PASS。发布基础设施脚本只在临时本地 fixture 仓库测试 Tag/分支竞争，未为本项目创建 Tag/Release 或改变正式版本。
- Data 集成通过：真实版本关联、忽略规则不裁剪 Plan、基准 DOCX 缺失仍使用历史 Snapshot、分析不写历史/工作目录、同哈希重新关联路径、原文件变化阻断、当前 Working Copy 的新映射、外部修改保留、Pending journal 不自动恢复、无关联旧历史/部分解析/不存在的记录不猜测身份。
- 正式工作台通过新 Core 契约/DI 接入，不增加业务 schema、Avalonia/Windows API 到核心层或新产品依赖。首次分析选择为空；Eligible 项目才能勾选，表格分类包含 Cell，失败重新分析清除旧 Plan/选择；恢复预览不改审阅状态和原 Comparison 事实。
- 实际 Debug 隔离 GUI：生成式 400/508 段 DOCX、368 项 Comparison 变化，分析得到 202 项格式恢复预览。全部/字符类型选择 202，实际取消单项后 201；实际窄窗、属性预览、返回审阅、普通关闭和重开通过。重开重新分析仍为 202，选择回到空；GUI 前、后和重启后的全表逻辑摘要、原 DOCX SHA 相同，integrity_check=ok / foreign_key_check 无错误，工作文件与恢复操作仍为零。没有触碰正常用户的安装或合同数据。
- 初次后台启动捕获为黑图，已显示窗口后重新取证，黑图不计通过；UI Automation 调用参数适配与独立 helper 分析器警告不属于产品结果。实际窄窗回读为物理 1062×651；不将其混同于 Headless 的逻辑 840×520 最小窗口。
- 证据位于 `artifacts/stage-c-20261005/`：TRX、before/after/restart JSON、UI tree、截图与 GUID 隔离路径。只读预览不代表确认执行、Undo/外部编辑选择、DOCX 导出或最终 Test Artifact 已验收；C2–C4 保持待完成。
