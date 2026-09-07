param(
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-DotNetCommand {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    Write-Host "dotnet $($Arguments -join ' ')"
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet command failed with exit code $($LASTEXITCODE): dotnet $($Arguments -join ' ')"
    }
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET SDK was not found. Install .NET 10 SDK before verification.'
}

$sdks = @(& dotnet --list-sdks)
if ($LASTEXITCODE -ne 0) {
    throw "dotnet --list-sdks failed with exit code $($LASTEXITCODE)."
}
if (-not ($sdks | Where-Object { $_ -match '^10\.' })) {
    throw "A .NET 10 SDK is required. Installed SDKs: $($sdks -join '; ')"
}

$repo = Split-Path -Parent $PSScriptRoot
$out = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    Join-Path $repo 'artifacts\verify\win-x64'
} else {
    [System.IO.Path]::GetFullPath($OutputDirectory)
}

$solution = Join-Path $repo 'XDiskInspector.sln'
$tests = Join-Path $repo 'tests\XDiskInspector.Tests\XDiskInspector.Tests.csproj'
$app = Join-Path $repo 'src\XDiskInspector.App\XDiskInspector.App.csproj'

Write-Host '=== X C盘巡检官 Windows verification ==='
Write-Host "Repository: $repo"
Write-Host "SDK: $($sdks -join '; ')"

Invoke-DotNetCommand -Arguments @('restore', $solution)
Invoke-DotNetCommand -Arguments @('test', $tests, '-c', 'Release', '--no-restore', '--logger', 'console;verbosity=normal')
Invoke-DotNetCommand -Arguments @('build', $app, '-c', 'Release', '--no-restore')

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Path $out | Out-Null

Invoke-DotNetCommand -Arguments @(
    'publish', $app,
    '-c', 'Release',
    '-r', 'win-x64',
    '--self-contained', 'true',
    '--no-restore',
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:DebugType=None',
    '-p:DebugSymbols=false',
    '-o', $out
)

$exe = @(Get-ChildItem $out -Filter 'XDiskInspector.App.exe' -File)
if ($exe.Count -ne 1) {
    throw "Verification expected exactly one XDiskInspector.App.exe, found $($exe.Count)."
}
if ($exe[0].Length -le 0) {
    throw 'Verification found an empty XDiskInspector.App.exe.'
}

$managedSidecars = @(Get-ChildItem $out -File | Where-Object { $_.Extension -in '.dll', '.pdb' })
if ($managedSidecars.Count -gt 0) {
    throw "Single-file verification found unexpected DLL/PDB sidecars: $($managedSidecars.Name -join ', ')"
}


# --- EXE startup smoke test: the app must launch and create its main window ---
Write-Host ''
Write-Host '=== EXE startup smoke test ==='
$smokeProc = Start-Process -FilePath $exe[0].FullName -WorkingDirectory $out -PassThru
$smokeOk = $false
$smokeError = $null
try {
    $deadline = (Get-Date).AddSeconds(45)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
        $alive = @(Get-Process -Name 'XDiskInspector.App' -ErrorAction SilentlyContinue)
        if ($alive.Count -eq 0) {
            $smokeProc.Refresh()
            $smokeError = "EXE exited before creating a main window (exit code $($smokeProc.ExitCode))."
            break
        }
        if (@($alive | Where-Object { $_.MainWindowHandle -ne 0 }).Count -gt 0) {
            $smokeOk = $true
            break
        }
    }
    if (-not $smokeOk -and -not $smokeError) {
        $smokeError = 'EXE did not create a main window within 45 seconds.'
    }
}
finally {
    Get-Process -Name 'XDiskInspector.App' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}
if (-not $smokeOk) {
    throw "EXE startup smoke test failed: $smokeError"
}
Write-Host 'EXE startup smoke test PASSED: main window created.'
Write-Host ''
Write-Host 'VERIFICATION PASSED'
Write-Host "EXE: $($exe[0].FullName)"
Write-Host "Size: $($exe[0].Length) bytes"
