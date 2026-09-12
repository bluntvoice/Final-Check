# Final Check

Final Check 是一款面向合同版本更迭、修订比对和格式整理场景的本地桌面工具。

## 当前状态

<!-- release-readme:current-version:start -->
当前正式版本：尚未发布。
<!-- release-readme:current-version:end -->

<!-- release-readme:summary:start -->
Final Check v0.1.0 当前处于早期开发阶段，Document Engine v0 与 Comparison Engine v0 已完成，尚未提供稳定发布版本或正式业务 UI。
<!-- release-readme:summary:end -->

## 技术栈

- .NET 10 LTS / C#
- Avalonia UI 12 / MVVM
- Open XML SDK 3.5.1
- SQLite / Entity Framework Core 10
- xUnit

官方开发和主要测试以 Windows 为主。项目架构尽量保持跨平台兼容，允许社区用户自行从源码构建 macOS 等平台版本；非 Windows 平台当前不属于官方发布与质量保证范围。

## 开发

需要 .NET 10 SDK：

```powershell
dotnet tool restore
dotnet restore FinalCheck.sln
dotnet build FinalCheck.sln
dotnet test FinalCheck.sln
dotnet run --project src/FinalCheck.Desktop/FinalCheck.Desktop.csproj
```

完整环境、项目结构和发布测量命令见 [`docs/development/README.md`](docs/development/README.md)。

## 计划核心功能

- 合同模板与多轮版本管理
- DOCX 文本、修订、批注、格式和表格差异识别
- 相同修改规则归并
- 原模板格式恢复
- 修改清单、修改说明和历史比对
- 数据备份与恢复

Final Check 解决“改了什么、哪里改了、怎么整理、格式怎么恢复、版本怎么追踪”，不承担合同法律风险分析。

## 文档

- [Product Requirements](docs/PRD/PRD-v0.1.0.md)
- [Architecture](docs/architecture/README.md)
- [ADR-0001](docs/architecture/ADR-0001-technology-stack.md)
- [Development](docs/development/README.md)
- [Performance Baseline](docs/development/performance-baseline.md)
- [Release Process](docs/development/release-process.md)
- [Changelog](CHANGELOG.md)

## Version

唯一版本来源为 `Directory.Build.props`，可运行 `./scripts/version.ps1 print` 查看。开发版本、Internal Test Build 和正式 Release 的区别见 [Release process](docs/development/release-process.md)；源码版本号不代表已经创建 GitHub Release。

## License

本项目采用 [MIT License](LICENSE)。
