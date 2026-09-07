# Windows Self-hosted Runner 验收指南

本项目可以使用你自己的 Windows 10/11 电脑执行 GitHub Actions，从而不依赖 GitHub-hosted Windows runner。

> 建议仅把 self-hosted runner 连接到你信任的私有仓库。不要在不受信任的公开 PR 上执行任意代码。

## 1. 在 GitHub 中创建 Runner

进入仓库：

`Settings → Actions → Runners → New self-hosted runner`

选择：

- Runner image：Windows
- Architecture：x64

GitHub 页面会生成一组专属于当前仓库/账户状态的注册命令和临时注册 Token。请直接使用页面生成的命令，不要把 Token 提交到仓库、Issue、PR、日志或截图中。

## 2. 建议的本机目录

建议在系统盘以外创建独立目录，例如：

```powershell
New-Item -ItemType Directory -Force D:\GitHubRunner\X-C-
Set-Location D:\GitHubRunner\X-C-
```

然后在这个目录中执行 GitHub 页面提供的下载、解压和 `config.cmd` 注册命令。

注册完成后，Runner 应至少具有 GitHub 默认标签：

- `self-hosted`
- `Windows`
- `X64`

项目的工作流正是使用这三个标签进行匹配。

## 3. 启动 Runner

完成 `config.cmd` 后，可以先前台运行：

```powershell
.\run.cmd
```

看到 Runner 显示为 GitHub 仓库中的 `Idle` 后即可执行验收。

如果你希望长期使用，可按 GitHub 页面/Runner 自带说明将其安装成 Windows 服务。首次验收建议先用 `run.cmd` 前台运行，便于观察日志。

## 4. 运行项目验收

进入：

`Actions → Self-hosted Windows CI → Run workflow`

选择分支：

`feat/xdiskinspector-v1`

然后点击 **Run workflow**。

工作流会执行：

1. Checkout
2. 安装/选择 .NET 10 SDK
3. `scripts/verify-win.ps1`
4. `dotnet restore`
5. xUnit 测试
6. WPF Release build
7. `win-x64` self-contained single-file publish
8. 检查 `XDiskInspector.App.exe` 存在且非空
9. 上传 Artifact：`X-C盘巡检官-self-hosted-win-x64`

## 5. 本机不经过 GitHub Actions 的直接验收

如果 Runner 还没有注册好，也可以直接在仓库目录执行：

```powershell
.\scripts\verify-win.ps1
```

要求本机已有 .NET 10 SDK。

默认输出：

`artifacts\verify\win-x64\XDiskInspector.App.exe`

## 6. 安全注意事项

- 不要把 Runner 注册 Token 写进脚本、README 或提交历史。
- 不要截图公开包含注册 Token 的 GitHub Runner 配置页面。
- Runner 工作目录中会执行仓库代码；只对可信分支/可信代码运行。
- 本项目的 self-hosted workflow 只有 `contents: read` 权限，并且只允许手动触发。
- 完成验收后，如果不再需要 Runner，可在 `Settings → Actions → Runners` 中移除，并删除本机 Runner 目录。

## 7. 验收通过标准

只有下面四项全部成立，PR 才应从 Draft 进入正式合并阶段：

- xUnit 全部通过；
- WPF Release build 成功；
- `win-x64` 单文件发布成功；
- 生成的 `XDiskInspector.App.exe` 存在且大小大于 0。
