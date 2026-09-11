param(
    [switch]$Publish,
    [switch]$Installer,
    [switch]$Zip,
    [switch]$SelfContained,
    [string]$AppVersion = "0.0.3",
    [string]$Configuration = "Release"
)

$cleanVersion = $AppVersion.TrimStart('v', 'V')

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  CliManager 构建脚本 (.NET 8 LTS)  " -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# 1. 运行所有单元测试
Write-Host "`n[1/3] 正在运行测试套件..." -ForegroundColor Yellow
dotnet test tests/CliManager.Tests/CliManager.Tests.csproj -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) {
    Write-Error "单元测试失败！"
    exit $LASTEXITCODE
}
Write-Host "✅ 单元测试全部通过！" -ForegroundColor Green

# 2. 编译解决方案
Write-Host "`n[2/3] 正在编译解决方案..." -ForegroundColor Yellow
dotnet build CliManager.slnx -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) {
    Write-Error "编译失败！"
    exit $LASTEXITCODE
}
Write-Host "✅ 编译成功！" -ForegroundColor Green

# 3. 发布
if ($Publish -or $Installer -or $Zip) {
    $outDir = "artifacts/publish"
    Write-Host "`n[3/3] 正在发布应用至 $outDir (Version: $cleanVersion) ..." -ForegroundColor Yellow
    
    # 停止正在运行的实例以避免文件锁占用
    Get-Process CliManager.App -ErrorAction SilentlyContinue | Stop-Process -Force
    
    # 清理旧发布目录以确保产物完全重新生成
    if (Test-Path $outDir) {
        Write-Host "正在清理旧发布目录: $outDir ..." -ForegroundColor Gray
        Remove-Item -Recurse -Force $outDir
    }
    
    # 默认采用自包含发布（包含运行时），杜绝系统 DOTNET_ROOT 指向旧版本 runtime 的报错
    $selfContainedArg = if ($PSBoundParameters.ContainsKey('SelfContained') -and -not $SelfContained) { "--self-contained false" } else { "--self-contained true" }
    
    Invoke-Expression "dotnet publish src/CliManager.App/CliManager.App.csproj -c $Configuration -r win-x64 $selfContainedArg -p:Version=$cleanVersion -o $outDir --nologo"
    
    if ($LASTEXITCODE -ne 0) {
        Write-Error "发布失败！"
        exit $LASTEXITCODE
    }
    
    Write-Host "✅ 发布完成！产物路径: $outDir" -ForegroundColor Green
}

# 4. 生成便携版 ZIP 压缩包
if ($Zip) {
    $releaseDir = "artifacts/release"
    if (-not (Test-Path $releaseDir)) { New-Item -ItemType Directory -Path $releaseDir -Force }
    $zipPath = Join-Path $releaseDir "CliManager-v$cleanVersion-win-x64-portable.zip"
    Write-Host "`n[Zip] 正在生成便携版压缩包至 $zipPath ..." -ForegroundColor Yellow
    if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
    Compress-Archive -Path "artifacts/publish/*" -DestinationPath $zipPath -CompressionLevel Optimal
    Write-Host "✅ 便携版压缩包生成完成！产物路径: $zipPath" -ForegroundColor Green
}

# 5. 生成 EXE 安装包
if ($Installer) {
    Write-Host "`n[Installer] 正在使用 Inno Setup 生成安装包 (Version: $cleanVersion)..." -ForegroundColor Yellow
    
    $isccCandidates = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe"
    )
    $iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $iscc) {
        $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
        if ($cmd) { $iscc = $cmd.Source }
    }
    
    if (-not $iscc) {
        Write-Error "未找到 Inno Setup 编译器 (ISCC.exe)！请先运行 winget install JRSoftware.InnoSetup 安装。"
        exit 1
    }
    
    $issFile = "installer/setup.iss"
    & "$iscc" "/DMyAppVersion=$cleanVersion" "$issFile"
    if ($LASTEXITCODE -ne 0) {
        Write-Error "安装包生成失败！"
        exit $LASTEXITCODE
    }
    Write-Host "✅ 安装包生成完成！产物路径: artifacts/release/CliManager-v$cleanVersion-Setup.exe" -ForegroundColor Green
}

if (-not ($Publish -or $Installer -or $Zip)) {
    Write-Host "`n提示: 可使用 .\build.ps1 -Publish 生成独立发布产物；`n      或 .\build.ps1 -Installer -Zip -AppVersion 0.0.3 一键生成发布压缩包与 EXE 安装包。" -ForegroundColor Gray
}
