# Phase 2 verification: Runner matrix - CLI types (.exe via powershell -File,
# .cmd, .bat) x special-char dirs x special-char args (plan 04 Phase 2).
# ASCII-only script; Chinese dir name built from code points.
$ErrorActionPreference = 'Stop'

$dotnet10 = 'C:\Program Files\dotnet'
if (Test-Path (Join-Path $dotnet10 'dotnet.exe')) {
    $env:DOTNET_ROOT = $dotnet10
    $env:PATH = "$dotnet10;$env:PATH"
}

$root = Split-Path $PSScriptRoot -Parent
$runner = Join-Path $root 'src\RightMenu.Runner\bin\Debug\net10.0-windows\RightMenu.Runner.exe'
if (-not (Test-Path $runner)) { throw "Runner not built: $runner" }

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

$outFile = Join-Path $env:TEMP 'rightmenu-matrix-out.txt'

# .ps1 dump for the .exe case (powershell.exe is a real .exe honoring WorkingDirectory)
$ps1 = Join-Path $base 'dump.ps1'
@(
    '$out = "CWD=" + (Get-Location).Path',
    'foreach ($a in $args) { $out += "`nARG=" + $a }',
    'Set-Content -Path (Join-Path $env:TEMP "rightmenu-matrix-out.txt") -Value $out -Encoding utf8'
) | Set-Content -Path $ps1 -Encoding ascii

# .cmd/.bat dump: 'cd >' writes the raw path without cmd re-parsing it
# (echo CWD=%CD% breaks on '&' and ')' in the path); quoted %* keeps args safe
$cmdBody = @(
    '@echo off',
    'chcp 65001 >nul',
    'cd > "%TEMP%\rightmenu-matrix-out.txt"',
    '(echo ARGS=%*)>> "%TEMP%\rightmenu-matrix-out.txt"'
)
$cmdScript = Join-Path $base 'dump.cmd'
$batScript = Join-Path $base 'dump.bat'
$cmdBody | Set-Content -Path $cmdScript -Encoding ascii
$cmdBody | Set-Content -Path $batScript -Encoding ascii

$exeArgs = @('a b', 'a&b', '50%', 'bang!', "it's", '(paren)')
$scriptArgs = @('a b', 'a&b', "it's", '(paren)', 'bang!')

$powershell = "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe"
$failed = 0
$total = 0

function Invoke-Case {
    param([string]$Label, [string]$Dir, [string[]]$RunnerArgs, [string[]]$ExpectLines)
    $script:total++
    Remove-Item $outFile -ErrorAction SilentlyContinue
    & $runner @RunnerArgs | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAIL  [$Label] runner exit $LASTEXITCODE" -ForegroundColor Red
        $script:failed++
        return
    }
    $content = @()
    if (Test-Path $outFile) { $content = Get-Content $outFile -Encoding utf8 | Where-Object { $_ -ne '' } }
    foreach ($expect in $ExpectLines) {
        if ($content -notcontains $expect) {
            Write-Host "FAIL  [$Label] missing [$expect] in [$($content -join ' / ')]" -ForegroundColor Red
            $script:failed++
            return
        }
    }
    Write-Host "PASS  [$Label]"
}

foreach ($d in $dirs) {
    # .exe case: powershell.exe -File dump.ps1 <args>
    $ra = @('--cwd', $d, '--exec', $powershell,
        '--arg', '-NoProfile', '--arg', '-ExecutionPolicy', '--arg', 'Bypass', '--arg', '-File', '--arg', $ps1)
    foreach ($a in $exeArgs) { $ra += @('--arg', $a) }
    $expect = @("CWD=$d") + ($exeArgs | ForEach-Object { "ARG=$_" })
    Invoke-Case ".exe  $d" $d $ra $expect

    # .cmd / .bat cases
    foreach ($ext in @('cmd', 'bat')) {
        $scriptPath = Join-Path $base "dump.$ext"
        $ra = @('--cwd', $d, '--exec', $scriptPath)
        foreach ($a in $scriptArgs) { $ra += @('--arg', $a) }
        $expectedArgs = 'ARGS=' + (($scriptArgs | ForEach-Object { '"' + $_ + '"' }) -join ' ')
        Invoke-Case ".$ext  $d" $d $ra @($d, $expectedArgs)
    }
}

Write-Host ''
if ($failed -gt 0) { throw "$failed of $total case(s) failed" }
Write-Host "All $total matrix cases passed." -ForegroundColor Green
