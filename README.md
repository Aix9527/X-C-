# X C盘巡检官

一个面向 Windows 的 C 盘空间解释与保守清理工具。它先告诉你“空间被什么占用、对应什么软件、删掉会发生什么”，再只允许经过规则库白名单和最终复核的清理目标进入执行清单。

> 作者：Aix · 抖音：xch03209527 · GitHub：Aix9527/Codex-Doctor

## 核心能力

- 深色 WPF 桌面界面，首页磁盘雷达/容量环。
- 单遍元数据扫描：一级目录容量、重点目录、默认 500 MB+ 大文件。
- 内置可扩展 JSON 软件/目录知识库，显示用途、风险、删除后果与建议。
- 默认不跟随重解析点、目录联接和符号链接。
- 允许列表清理：未知路径、个人数据、AI 模型、虚拟磁盘、系统关键目录不会因为“很大”就被删除。
- 清理前重新生成最终清单并再次校验路径、规则版本、文件时间和重解析点状态。
- 回收站属于不可恢复动作，必须单独勾选确认。
- 可在项目之间停止后续清理；正在进行的单文件删除不会被强制中断。
- JSON / HTML 扫描报告。
- Windows 官方系统优化入口：DISM、已安装应用设置、WSL/Docker 操作指导。

## 安全边界

默认拒绝直接清理：桌面/文档/下载/视频、WSL/Docker VHDX、AI 模型、`.venv`、`node_modules`、聊天/浏览器/应用数据库、`WinSxS`、`Windows\Installer`、驱动存储、未知路径。详见 [`docs/SAFETY.md`](docs/SAFETY.md)。

## 开发构建

要求：Windows 10/11 + .NET 10 SDK。

```powershell
dotnet restore .\XDiskInspector.sln
dotnet test .\tests\XDiskInspector.Tests\XDiskInspector.Tests.csproj -c Release
dotnet build .\src\XDiskInspector.App\XDiskInspector.App.csproj -c Release
```

## 发布单文件 EXE

```powershell
.\scripts\publish-win-x64.ps1
```

输出目录：`artifacts\publish\win-x64\`。发布配置为 `win-x64`、self-contained、single-file。

## 规则库

首版规则位于：

`src/XDiskInspector.Rules/directory-rules.json`

规则包含路径模式、软件名称、用途、完全删除后果、风险、建议、是否允许自动清理、清理类型和最小保留天数。清理引擎不会接受没有当前有效规则 ID 的目标。

## 设计与实施文档

- `docs/specs/2026-09-07-x-c-disk-inspector-design.md`
- `docs/superpowers/plans/2026-09-07-x-c-disk-inspector.md`
- `docs/USER_GUIDE.md`
- `docs/SAFETY.md`

## 隐私

扫描引擎只读取文件系统元数据，不读取文件内容。报告不记录浏览器凭据、API Key 或其他密钥。
