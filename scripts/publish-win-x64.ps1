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
    throw '.NET SDK was not found. Install .NET 10 SDK before publishing.'
}

$repo = Split-Path -Parent $PSScriptRoot
$out = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    Join-Path $repo 'artifacts\publish\win-x64'
} else {
    [System.IO.Path]::GetFullPath($OutputDirectory)
}

$solution = Join-Path $repo 'XDiskInspector.sln'
$tests = Join-Path $repo 'tests\XDiskInspector.Tests\XDiskInspector.Tests.csproj'
$app = Join-Path $repo 'src\XDiskInspector.App\XDiskInspector.App.csproj'

Invoke-DotNetCommand -Arguments @('restore', $solution)
Invoke-DotNetCommand -Arguments @('test', $tests, '-c', 'Release', '--no-restore', '--logger', 'console;verbosity=normal')

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
    throw "Expected exactly one XDiskInspector.App.exe, found $($exe.Count)."
}
if ($exe[0].Length -le 0) {
    throw 'Published XDiskInspector.App.exe is empty.'
}

$managedSidecars = @(Get-ChildItem $out -File | Where-Object { $_.Extension -in '.dll', '.pdb' })
if ($managedSidecars.Count -gt 0) {
    throw "Single-file publish found unexpected DLL/PDB sidecars: $($managedSidecars.Name -join ', ')"
}

Write-Host "Published: $($exe[0].FullName) ($($exe[0].Length) bytes)"
