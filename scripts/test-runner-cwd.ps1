# Phase 0 verification: Runner starts test CLI with correct WorkingDirectory
# across the special-character directory matrix (plan 04 Phase 2 preview).
# ASCII-only on purpose: avoids script-encoding issues; Chinese dir name is
# built from code points.
$ErrorActionPreference = 'Stop'

$dotnet10 = 'C:\Program Files\dotnet'
if (Test-Path (Join-Path $dotnet10 'dotnet.exe')) {
    $env:DOTNET_ROOT = $dotnet10
    $env:PATH = "$dotnet10;$env:PATH"
}

$root = Split-Path $PSScriptRoot -Parent
$runner = Join-Path $root 'src\RightMenu.Runner\bin\Debug\net10.0-windows\RightMenu.Runner.exe'
if (-not (Test-Path $runner)) { throw "Runner not built: $runner" }

# "han zhong-wen kong-ge" -> Chinese + spaces dir name
$zhName = [string][char]0x542B + ' ' + [char]0x4E2D + [char]0x6587 + ' ' + [char]0x7A7A + [char]0x683C
$base = 'C:\RightMenuTests'
$dirs = @(
    "$base\normal",
    "$base\$zhName",
    "$base\ampersand & dir",
    "$base\paren (test)",
    "$base\it's-valid",
    "$base\semi;colon"
)
foreach ($d in $dirs) { New-Item -ItemType Directory -Path $d -Force | Out-Null }

# Test CLI (.cmd): writes its cwd as UTF-8. Delayed expansion keeps '&' in
# !CD! from being parsed as a command separator; chcp 65001 makes the
# redirected output UTF-8 so PowerShell can read it back reliably.
$cmdScript = Join-Path $base 'echo-cwd.cmd'
@(
    '@echo off',
    'chcp 65001 >nul',
    'setlocal enabledelayedexpansion',
    '(echo CWD=!CD!)> "%TEMP%\rightmenu-cwd-test.txt"'
) | Set-Content -Path $cmdScript -Encoding ascii

$resultFile = Join-Path $env:TEMP 'rightmenu-cwd-test.txt'
$failed = 0
foreach ($d in $dirs) {
    Remove-Item $resultFile -ErrorAction SilentlyContinue
    & $runner --cwd $d --exec $cmdScript | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAIL  runner exit $LASTEXITCODE for [$d]" -ForegroundColor Red
        $failed++
        continue
    }
    $result = (Get-Content $resultFile -Raw -Encoding utf8 -ErrorAction SilentlyContinue)
    if ($result) { $result = $result.Trim() }
    $expected = "CWD=$d"
    if ($result -eq $expected) {
        Write-Host "PASS  $d"
    } else {
        Write-Host "FAIL  expected [$expected] got [$result]" -ForegroundColor Red
        $failed++
    }
}

if ($failed -gt 0) { throw "$failed case(s) failed" }
Write-Host 'All runner cwd cases passed.' -ForegroundColor Green
