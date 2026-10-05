# Final Check — Installer Wizard + Storage Settings v0

> Status: IN PROGRESS
> Stage: B
> Version: v0.1.0 development
> Started: 2026-10-04
> Baseline: `865c3fa1247cbfdc8f971861834dec5b3c820f43`

## 授权与阶段边界

用户于 2026-10-04 明确要求进入 Stage B。Stage A 已实现部分及验收证据保留；真实触控板等剩余人工门禁仍按 [Stage A task](usability-workflow-correction-v0.md) 记录，不因为进入 Stage B 改写成通过。

需求以 PRD 第 45、58.1、63.13、64 章为准；先阅读 Installer Architecture、ADR-0006、Storage and Paths、Release process 和现有 Storage Foundation 实现。复用非破坏迁移与已验证的 Velopack 1.2.0 链，不增加产品数据库/schema 迁移，不创建 Tag / Release。

## Phase 与完成条件

1. **B1 — Storage Settings：DONE**。正式设置入口、当前路径与分类统计、打开目录、选择/输入目标、验证与明确确认、取消/失败/恢复提示；复用真实 Foundation 复制/验证/热切换。Release build/test、隔离有数据 UI 迁移、原始文件/历史/旧位置保留及重启验证已通过，实现已 commit/push；最终实现 HEAD 的测试包与本机安装补验已完成，见下方证据。
2. **B2 — Installer Spike：PENDING**。在独立 Windows 用户或 VM 中验证原生 Setup 包装方案，覆盖可见目录/Browse/取消、C/D/E/中文空格、危险路径、单实例、维护、两次官方 UpdateManager 原地更新、快捷方式/Registry/卸载和独立 DataRoot/历史保留。开发环境和 Spike 原型可先准备，但缺失现场结果不能标为通过。
3. **B3 — Installer integration：PENDING / gated by B2**。仅完整 Spike 通过后落地生产向导、verifier 与 Test/Prerelease/Stable 共用打包入口；保留包 ID/channel/feed/nupkg/Portable 契约。最终 HEAD 的 CI、Test Build 和安装矩阵仍需验证。

每个独立 Phase 执行测试、同步任务/架构/开发文档、正常 commit/push 并回读远端后再推进。外部 CI/包验收证据保存在 Git 忽略的 artifacts 和交付报告，不为填写 Run ID 额外改变已构建 HEAD。

## B1 实现与验证

- 新增 `StorageSettingsViewModel` / `StorageSettingsView`，Desktop 注入已有 `IDataRootProvider`、usage、validator、migration。文件夹打开通过 Core 接口与 Infrastructure 平台实现；App 不依赖 Registry/Shell。
- 单独展示唯一物理总计和 Database 内 Snapshot/Comparison/restore 逻辑占用，部分统计有诊断，数据库读取失败不显示假零。迁移成功与统计刷新失败分别报告。
- 用户验证后明确看到源/目标和空间预算，再确认复制；目标改变或 generation 改变使确认失效。提交前取消由 Foundation 处理，提交后的取消不退回旧库。未知结果要求正常关闭后启动恢复，不推断提交状态。
- 迁移执行时导航/普通关闭暂缓，等待完成或取消完成后再关闭；没有百分比服务时显示不确定进度，不制造进度数值。Foundation 负责全局/跨进程屏障，UI 按钮不是数据锁。
- 现有安装/打包/版本基线保持；安装器门禁仍未完成，不能宣称 Beta 合格。
- 本地实现检查点 Release build：0 warning / 0 error；完整 Release 测试 340/340 PASS（Core 5 / Documents 45 / Comparison 52 / Data 149 / App 89）。新增覆盖关闭屏障及最小窗口确认区自动滚入。Data 测试 link 同一平台无关 ViewModel，以真实 SQLite/Working Copy/restore history 验证明确确认与即时重开；没有引入 Avalonia 或 Windows API 到 Data。
- 补充安全检查：服务抛出无法核实提交状态的异常时，UI 保持在存储页并阻止进入其他数据页面/读取最近比对，要求正常关闭重启；与 Foundation `NeedsReview` 写屏障配合。新增导航回归，最终 HEAD 须包含此检查。
- 含补充安全检查的最终本地 Release build：0 warning / 0 error；全量 341/341 PASS（Core 5 / Documents 45 / Comparison 52 / Data 149 / App 90）。此前实现 HEAD 的 Windows/macOS CI 和 Test Build 也通过，但它们不代替本安全修正 HEAD 的新包与安装补验。
- 真实 Debug 隔离窗口：生成 400/508 段 DOCX，真实自动项目/V1/V2/独立比对历史，共 368 项差异；GUI 点击验证、暂不更改、重新确认与实际迁移。热切换后全表逻辑摘要和原始 DOCX SHA 相同，旧数据保留，bootstrap generation 1→2，新路径立即生效。打开目录经 Explorer 实际路径回读，正常关闭/重启保持新位置并实际重新显示 368 项历史；总占用包含完整迁移数据库备份，不重复加逻辑 payload。
- 隔离启动脚本一度漏传私有 .NET SDK 的 DOTNET_ROOT，framework-dependent Debug apphost 未加载 coreclr，没有业务窗口；补齐环境后重启通过。未将其记录为自包含安装包或数据恢复失败。UI Automation 对“继续最近一次比对”等待结果导航控件后再取证，避免把读取中的首页当成历史回读成功。
- 最终实现 HEAD `17d0549a066c1fe7fb5fcd6a98db0b3daa4273a4` 的 [CI 37174527316](https://github.com/bluntvoice/Final-Check/actions/runs/37174527316) 与 [Test Build 37174529299](https://github.com/bluntvoice/Final-Check/actions/runs/37174529299) 均 success，2026-10-05 回读确认 head_sha 一致。内部测试版 `0.1.0-dev.17.1`（Artifact `11292074391`）下载摘要与 Actions digest 一致，逐文件/version/metadata/layout verifier PASS。
- 本机正常用户现有实例的原生 Setup 覆盖维护退出码 0，已安装程序集版本为 dev.17.1。安装前、启动试用与正常重启后的 bootstrap 摘要及 SQLite 全表逻辑摘要不变，integrity_check=ok，foreign_key_check 无错误，原有历史与原始文件保留。实际设置页、打开目录、Browse/取消、非空目录/源重叠阻断及窄窗已验证；没有试迁移正常用户合同数据。
- 最终包私有副本中的实际产品程序集 SHA 保持不变，以隔离生成数据完成 D 盘热切换及 D→F 固定盘中文/空格目录迁移，generation 1→2→3；每次正常重启后重新显示 368 项历史，全表逻辑摘要与原始 DOCX SHA 不变，旧位置保留。该验证程序不替代安装器生命周期验收；非空 Working Copy/restore history 另由真实 Data.Tests 集成用例验证。
- 2026-10-05 进入后续工作前补齐 B1 完成记录；此文档检查点不改变已验收二进制的源 HEAD。原始证据与完整报告位于 `artifacts/stage-b-20261004/acceptance-report.md`，不提交 DOCX、数据库或个人路径材料。

## 未完成门禁

安装器必须使用隔离 Windows 环境。当前普通本机安装可以用于存储页测试包补验；正常用户现有安装/合同数据不得用于自定义安装、连续更新和卸载的实验。没有隔离环境时继续准备与只读验证，保持 B2/B3 未通过。

Storage UI 的 Debug/Headless/集成测试不代替最终 HEAD 的真实安装版与物理盘迁移矩阵；后续现场验收区分实际执行、自动化模拟与未执行项目。
