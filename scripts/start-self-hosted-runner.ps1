param(
    [string]$RunnerDirectory = 'D:\GitHubRunner\X-C-'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Write-Host '=== X C盘巡检官 Release Runner 启动器 ===' -ForegroundColor Cyan
Write-Host "Runner directory: $RunnerDirectory"

if (-not (Test-Path $RunnerDirectory)) {
    Write-Host ''
    Write-Host '未找到 Runner 目录。' -ForegroundColor Yellow
    Write-Host '请先进入 GitHub 仓库：Settings -> Actions -> Runners -> New self-hosted runner'
    Write-Host '选择 Windows / x64，并按 GitHub 页面生成的命令完成一次性注册。'
    Write-Host ''
    Write-Host "建议目录：$RunnerDirectory"
    exit 2
}

$runCmd = Join-Path $RunnerDirectory 'run.cmd'
$configMarker = Join-Path $RunnerDirectory '.runner'

if (-not (Test-Path $runCmd)) {
    Write-Host "未找到 $runCmd" -ForegroundColor Red
    Write-Host '该目录看起来不是完整的 GitHub Actions Runner。'
    exit 3
}

if (-not (Test-Path $configMarker)) {
    Write-Host ''
    Write-Host 'Runner 尚未完成 config.cmd 注册。' -ForegroundColor Yellow
    Write-Host '请使用 GitHub Settings -> Actions -> Runners 页面当前生成的临时 Token 注册。'
    Write-Host '不要把注册 Token 写入仓库、脚本、Issue、日志或截图。'
    exit 4
}

Write-Host ''
Write-Host '正在启动 self-hosted Windows x64 Runner…' -ForegroundColor Green
Write-Host '保持弹出的 Runner 窗口运行；当 GitHub 显示 Listening for Jobs / Idle 后，排队中的 Release 会自动开始。'

$cmdArgs = "/k `"cd /d `"`"$RunnerDirectory`"`" && call run.cmd`""
Start-Process -FilePath 'cmd.exe' -ArgumentList $cmdArgs -WorkingDirectory $RunnerDirectory

Write-Host 'Runner 窗口已启动。' -ForegroundColor Green
Write-Host '可在 GitHub Actions 中查看：Release Windows x64'
