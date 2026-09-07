param(
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$out = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    Join-Path $repo 'artifacts\publish\win-x64'
} else {
    [System.IO.Path]::GetFullPath($OutputDirectory)
}

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Path $out | Out-Null

dotnet restore (Join-Path $repo 'XDiskInspector.sln')
dotnet test (Join-Path $repo 'tests\XDiskInspector.Tests\XDiskInspector.Tests.csproj') -c Release --no-restore
dotnet publish (Join-Path $repo 'src\XDiskInspector.App\XDiskInspector.App.csproj') -c Release -r win-x64 --self-contained true --no-restore -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $out

$exe = @(Get-ChildItem $out -Filter 'XDiskInspector.App.exe')
if ($exe.Count -ne 1) { throw "Expected exactly one XDiskInspector.App.exe, found $($exe.Count)." }
Write-Host "Published: $($exe[0].FullName)"
