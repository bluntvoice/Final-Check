# Comparison UI v0

## 边界与入口

Quick Compare 是不要求项目、模板或版本链的独立入口：首页 → 新建比对 → 基准版本 / 当前版本。后续项目模式可复用该工作流，不提前建立完整项目 UI。

## Session 与输入适配

`ComparisonSession` 在 App/ViewModel 层保存会话 ID、两侧外部文件身份、解析/执行状态、结果 ID、时间、错误与诊断。`ComparisonSetupViewModel` 管理选择、替换、移除与按钮可用性；View code-behind 仅适配 Avalonia StorageProvider 和 DragDrop，不解析 DOCX、不访问 SQLite。

`IComparisonFileInspector` 的 Infrastructure 实现在后台只读校验后缀、路径、权限和 SHA-256，并返回名称、绝对路径、大小与修改时间。原始文件不复制到 DataRoot、不设置只读、不改写。每侧一次一个 DOCX；无效选择不替换已有有效输入。执行期间输入锁定，避免会话角色变化。

## 实施状态

Phase 1 为入口与文件会话基础；完整工作流服务、历史、结果管理与预览将在 Phase 2–5 按 task 逐阶段接入。入口基础不代表完整比对已可试用，真实状态见 [task](../tasks/comparison-ui-v0.md)。
