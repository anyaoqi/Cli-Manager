<#
.SYNOPSIS
    RightMenu build script: clean / build / test / publish with per-step timing (plan 04 Phase 0).
.EXAMPLE
    ./build.ps1                # build + test
    ./build.ps1 -Clean         # clean first
    ./build.ps1 -Publish       # self-contained publish of App and Runner
#>
[CmdletBinding()]
param(
    [switch]$Clean,
    [switch]$Publish,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

# This machine's PATH/DOTNET_ROOT points to C:\Users\admin\Tools\dotnet (8.x only).
# .NET 10 lives in C:\Program Files\dotnet; override for this session only.
$dotnet10 = 'C:\Program Files\dotnet'
if (Test-Path (Join-Path $dotnet10 'dotnet.exe')) {
    $env:DOTNET_ROOT = $dotnet10
    $env:PATH = "$dotnet10;$env:PATH"
}
$sdkVersion = (& dotnet --version)
if (-not $sdkVersion.StartsWith('10.')) {
    throw "Requires .NET 10 SDK, resolved $sdkVersion. Check DOTNET_ROOT/PATH."
}

$timings = [ordered]@{}
function Invoke-Step {
    param([string]$Name, [scriptblock]$Action)
    Write-Host "==> $Name" -ForegroundColor Cyan
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    & $Action
    if ($LASTEXITCODE -ne 0) { throw "step '$Name' failed (exit code $LASTEXITCODE)" }
    $sw.Stop()
    $timings[$Name] = $sw.Elapsed
    Write-Host ("<== {0} done in {1:F1}s" -f $Name, $sw.Elapsed.TotalSeconds) -ForegroundColor Green
}

$sln = Join-Path $root 'RightMenu.slnx'

if ($Clean) {
    Invoke-Step 'clean' { dotnet clean $sln -v minimal --nologo }
}

Invoke-Step 'build (Debug)' { dotnet build $sln -c Debug -v minimal --nologo }

if (-not $SkipTests) {
    Invoke-Step 'test' { dotnet test $sln -c Debug --no-build -v minimal --nologo }
}

if ($Publish) {
    $publishDir = Join-Path $root 'artifacts\publish'
    Invoke-Step 'publish App' {
        dotnet publish (Join-Path $root 'src\RightMenu.App\RightMenu.App.csproj') `
            -c Release -r win-x64 --self-contained true -o $publishDir -v minimal --nologo
    }
    Invoke-Step 'publish Runner' {
        dotnet publish (Join-Path $root 'src\RightMenu.Runner\RightMenu.Runner.csproj') `
            -c Release -r win-x64 --self-contained true -o $publishDir -v minimal --nologo
    }
    $sizeMB = (Get-ChildItem $publishDir -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
    Write-Host ("publish dir size: {0:F1} MB -> {1}" -f $sizeMB, $publishDir) -ForegroundColor Yellow
}

Write-Host ''
Write-Host '===== timing summary =====' -ForegroundColor Cyan
foreach ($entry in $timings.GetEnumerator()) {
    Write-Host ("{0,-18} {1,8:F1}s" -f $entry.Key, $entry.Value.TotalSeconds)
}
