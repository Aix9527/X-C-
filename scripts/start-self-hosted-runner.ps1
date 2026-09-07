param(
    [string]$RunnerDirectory = 'D:\GitHubRunner\X-C-'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Write-Host '=== X Disk Inspector Release Runner Launcher ===' -ForegroundColor Cyan
Write-Host "Runner directory: $RunnerDirectory"

if (-not (Test-Path $RunnerDirectory)) {
    Write-Host ''
    Write-Host 'Runner directory was not found.' -ForegroundColor Yellow
    Write-Host 'Open GitHub repository: Settings -> Actions -> Runners -> New self-hosted runner'
    Write-Host 'Choose Windows / x64 and complete the one-time registration using GitHub generated commands.'
    Write-Host ''
    Write-Host "Recommended directory: $RunnerDirectory"
    exit 2
}

$runCmd = Join-Path $RunnerDirectory 'run.cmd'
$configMarker = Join-Path $RunnerDirectory '.runner'

if (-not (Test-Path $runCmd)) {
    Write-Host "run.cmd was not found: $runCmd" -ForegroundColor Red
    Write-Host 'This directory does not look like a complete GitHub Actions Runner installation.'
    exit 3
}

if (-not (Test-Path $configMarker)) {
    Write-Host ''
    Write-Host 'Runner is not registered yet (missing .runner).' -ForegroundColor Yellow
    Write-Host 'Use the temporary registration token currently shown in GitHub Settings -> Actions -> Runners.'
    Write-Host 'Do not store the registration token in the repository, scripts, issues, logs, or screenshots.'
    exit 4
}

Write-Host ''
Write-Host 'Starting self-hosted Windows x64 Runner...' -ForegroundColor Green
Write-Host 'Keep the Runner window open. When it shows Listening for Jobs / Idle, the queued Release job will start automatically.'

$cmdArgs = "/k `"cd /d `"`"$RunnerDirectory`"`" && call run.cmd`""
Start-Process -FilePath 'cmd.exe' -ArgumentList $cmdArgs -WorkingDirectory $RunnerDirectory

Write-Host 'Runner window started.' -ForegroundColor Green
Write-Host 'Check GitHub Actions: Release Windows x64'
