# Windows Installer Architecture / Spike

- 日期：2026-09-12
- 状态：需求与方案已落档；安装器 Spike / 产品实现尚未执行。
- 基线：Velopack NuGet / vpk **1.2.0**，包 ID `FinalCheck.App`。

## 正式产品契约与当前差距

首次安装必须显示完整安装目录，提供“浏览”和修改入口，用户确认后才写入。推荐目录可以继续是 `%LocalAppData%\FinalCheck.App`，但不得无交互直接采用；允许本机可写目录，例如 `D:\Applications\Final Check`、`E:\Programs\Final Check`。安装路径与 DataRoot 分开选择，禁止任一业务数据根目录与安装根目录重叠。

Test / Prerelease / Stable 使用同一目录选择、现有实例检测和验证代码，仅版本/channel 不同。同一用户的同一 `FinalCheck.App` 不提供多目录 side-by-side 安装。已有实例的覆盖安装/升级显示并沿用实际安装位置，不重新采用默认 C 盘路径；更换已安装程序目录不是本阶段功能。卸载只处理经确认的实际安装根目录、该实例快捷方式和注册信息，保留独立 DataRoot 与 bootstrap。

当前 `scripts/build-windows-package.ps1` 仅调用原生 `vpk pack`，没有 `--msi`、Final Check Wizard 或路径选择 UI。现有 Setup 已通过默认路径安装/启动/卸载验证，**该证据不覆盖自定义路径、连续更新和交互式浏览**。本轮不修改脚本、workflow、依赖或现有产物。

## 1.2.0 能力调查

调查同时核对本机固定版本 `dotnet tool run vpk -- pack --help` 与上游 `1.2.0` 源码；tag commit 为 `f2edcbcafb81da5b3c884aaea330e225ad91d8b6`。在线文档可能先于稳定版本更新，不能只凭当前文档推断固定版本行为。

| 机制 | 已确认能力 | 对本需求的结论 |
|---|---|---|
| 原生 `Setup.exe` | one-click；`--installto <DIR>` 把安装根目录传给官方安装器 | 支持指定路径，不提供用户目录选择向导，不能单独验收 |
| `vpk pack --msi --instLocation ...` | 可生成 MSI，PerUser / PerMachine / Either 表达安装范围 | Either 的范围选择不是任意目录浏览；现有项目未生成 MSI |
| MSI `VELOPACK_INSTALLDIR` | 1.2.0 模板声明 Secure property，设置 `INSTALLFOLDER`，UI / execute 分支可接收指定目录 | 是 MSI 属性，不是环境变量驱动的产品 UI，也不适用于原生 Setup 的同名参数 |
| MSI Browse dialog | 模板包含 `BrowseDlg` 资源和校验事件；正常 Welcome / Scope → VerifyReady 导航未提供启动 Browse 的按钮 | 源码审查不能证明存在可达的目录选择页面；不能以资源存在假称功能可用，仍需实际 UI Spike |
| `WindowsVelopackLocator` | 从当前进程、父目录 `Update.exe`、`current/sq.version` 定位 `RootAppDir` | 自定义根目录具备技术基础；更新必须使用此实际根目录，不能重新拼默认目录 |
| 官方安装/注册 | 官方安装器依 locator 创建文件/快捷方式/卸载项；Registry 保存实际 `InstallLocation` | 可以保留官方生命周期，但需验证自定义路径后的升级、覆盖安装与卸载全链路 |

上游 #945 报告 **vpk / NuGet 1.2.0** 的 PerUser MSI passive/quiet 模式默认落在 `C:\{packId}`；#970 是随后修复，不能假定已回补本仓库的固定版本。#985 的 portable/MSI launcher 命名修复也晚于 1.2.0，与本项目 packTitle / mainExe 不同有关。#997 对 MSI 产品注册在 Velopack 更新后的协调提出问题；这是未解决的上游风险信号，不是本机实测结论。MSI 的文件组件/repair 与 Velopack 文件替换形成两套所有权，不能未经验证切换。

## 推荐：轻量 Wizard 包装原生 Setup

优先 Spike **Final Check 安装 Wizard → 未修改的 Velopack Setup → 原有 Velopack layout / updater**。不重写安装、更新、卸载引擎，不复制 MSI 文件所有权模型，不 fork 上游模板作为首选。

Wizard 的职责限于：

1. 验证内嵌官方 Setup 的版本、包身份、SHA-256；签名覆盖包装层和载荷，checksum 不能代替来源认证。
2. 获取安装互斥锁，以当前用户注册信息查找现有实例，并核对根目录 `current/sq.version` 的包 ID、实际 Update.exe 和快捷方式目标。Registry 属于平台实现，不能进入 Core/Data。发现多个实例、失效注册或身份矛盾时停止，不能猜测目录或自行卸载。
3. 首装显示推荐完整目录、文本编辑和“浏览”；已有实例显示实际目录并固定为原地维护，不允许用户通过新目录创建第二实例。再次检查安装目录与 bootstrap 指向的 DataRoot/已保留旧数据位置不重叠。
4. 验证绝对路径、固定磁盘、当前用户写入/重命名权限、剩余空间、空目录或已确认本应用实例；拒绝其他非空目录、盘根、危险路径、链接/重解析点和网络目录。即使 manifest 是本应用，也须检查安装根目录是否混入业务/未知用户文件，出现异常先停止维护，不让官方覆盖流程删除这些文件。不得让原生 Setup 的覆盖流程处理未知用户目录。
5. 用户明确确认后，用进程参数列表（不拼 shell 命令）调用原生 Setup 的官方 `--installto`，内部可用 `--silent` 避免第二层 UI/提前启动。隐藏内部执行不等于省略外层可见确认。用户取消不得开始安装。
6. 检查退出状态及实际 manifest/版本/layout/Registry/快捷方式；成功后由 Wizard 提供启动选项，失败报告阶段和原因。不自行删除未知文件或修补注册信息来掩盖失败。

更新仍使用 `.nupkg` / `releases.<channel>.json` 和实际 locator 的根目录，不能把首次安装 Wizard 当作每次更新引擎。固定 PackageId、mainExe、hooks 和 channel 契约保持不变。可写的 per-user 路径是首选；受保护 Program Files / per-machine 提权不是本方案首阶段支持范围。若安装根目录后来失去写权限，应诊断并验证官方提权/恢复行为，绝不能回退到 C 盘重装。

包装层必须无浏览器 Runtime、无额外大型运行时、无需用户另装 .NET。实现工具在 Spike 中比较小型 C# 独立 bootstrapper（如安装器专用 NativeAOT 可行性）与最小原生 Windows Wizard 的体积/签名/toolchain；这不是启用应用 NativeAOT 或批准引入新 SDK。本轮不选择未经实测的新工具链。若原生 Setup 包装无法通过链路验证，再评估正式稳定上游升级及受控 MSI/WiX UI 包装方案；不得偷偷改依赖版本。

Velopack 自管 `packages`、Update.exe、manifest 是安装/更新载荷，不是合同数据或应用 Backup，不能搬入 DataRoot 或删掉来降低体积。上游 locator 还可能使用 `%LocalAppData%\velopack` 日志、不可写根目录时的 updater cache fallback；其位置与行为需要单独审计，不能宣称“全部运行文件已可移动”。业务 SQLite/缓存/日志绝不能以此为借口存入安装目录。

## Spike 与发布接入门禁（尚未执行）

在隔离 Windows VM / 测试用户执行，不拿正常用户数据或真实合同试装；保留现有原生包装作为回归基线。记录精确源 SHA、工具版本、路径、安装范围、hash、exit code、日志及每个检查的真实结果。

| 检查组 | 必须覆盖 |
|---|---|
| UI / 路径 | 默认目录可见、Browse 可用、取消无安装；D/E 盘、空格、中文路径、拒绝未知非空/只读/网络/链接/低空间目录 |
| 单实例 / 覆盖 | 旧默认安装被识别；新自定义安装被识别；同版本维护/新版 Setup 沿用实际路径；选择另一目录被阻止；并发两个 Setup 不产生双实例 |
| 连续更新 | 同一自定义目录中版本 A→B→C 至少两次官方 UpdateManager 更新；退出/重启、channel 转换及失败重试；C 盘没有新应用副本；原目录版本与快捷方式持续正确 |
| 注册 / 卸载 | HKCU InstallLocation / UninstallString 指向实际根目录；Apps & Features、Desktop/StartMenu 快捷方式正确；更新后的普通卸载完整，DataRoot/bootstrap 不变 |
| 数据安全 | 默认/自定义 DataRoot 的 SQLite logical digest、Snapshot/Comparison/restore 历史、Working Copy、原始 DOCX 与 bootstrap 在安装/更新/卸载中不被破坏；hooks 不访问数据库 |
| Packaging | 包 ID/channel/feed/nupkg 不变；公开 Setup 名称与 assets 清单正确；外层及内层版本/hash/签名可验证；无重复载荷/额外 Runtime；既有 CI 与三类包统一机制 |

只有全部门禁通过后，才能单独提交实际包装器、package verifier 和三种包入口接入，并重新执行 Windows Test Build。原生中间 Setup 不作为普通用户可绕过向导的替代入口；公开 Setup 保持既有友好命名，内部载荷另外校验，updater feed 不改。Portable 不属于安装器，保留原 layout；不提供第二个注册安装实例，也不作为数据隔离承诺。

现有 Test Artifact 可继续用于内部回归，但必须明确未满足新安装 UI 要求。正式面向用户的 Test / Prerelease / Stable 目录机制验收在 Spike 完成前不能标记通过；本轮不创建 Tag / Release，也不改现有 workflow 来伪造门禁已自动化。

## 官方依据（访问日期 2026-09-12）

- [Velopack installer 文档](https://docs.velopack.io/packaging/installer)：one-click 与 MSI / install location 能力。
- [1.2.0 MSI template](https://github.com/velopack/velopack/blob/1.2.0/src/vpk/Velopack.Packaging.Windows/Msi/Templates/MsiTemplate.hbs)、[InstallScopeDlg](https://github.com/velopack/velopack/blob/1.2.0/src/vpk/Velopack.Packaging.Windows/Msi/Templates/InstallScopeDlg.wxs)：目录属性及页面导航。
- [1.2.0 install.rs](https://github.com/velopack/velopack/blob/1.2.0/src/bins/src/commands/install.rs)、[Windows locator](https://github.com/velopack/velopack/blob/1.2.0/src/lib-csharp/Locators/WindowsVelopackLocator.cs)、[Registry](https://github.com/velopack/velopack/blob/1.2.0/src/bins/src/windows/registry.rs)：实际根目录和官方生命周期。
- [MSI custom directory #916](https://github.com/velopack/velopack/pull/916)、[MSI passive/quiet #945](https://github.com/velopack/velopack/issues/945)、[launcher #985](https://github.com/velopack/velopack/pull/985)、[MSI registration #997](https://github.com/velopack/velopack/issues/997)：已纳入后续验证的限制/风险。

相关决策见 [ADR-0006](ADR-0006-install-location-and-data-root.md)，数据路径契约见 [storage-and-paths.md](storage-and-paths.md)。
