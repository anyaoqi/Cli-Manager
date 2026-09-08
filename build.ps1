param(
    [switch]$Publish,
    [switch]$SelfContained,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  CliManager 构建脚本 (.NET 10)  " -ForegroundColor Cyan
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
if ($Publish) {
    $outDir = "artifacts/publish"
    Write-Host "`n[3/3] 正在发布应用至 $outDir ..." -ForegroundColor Yellow
    
    # 默认采用自包含发布（包含运行时），杜绝系统 DOTNET_ROOT 指向旧版本 runtime 的报错
    $selfContainedArg = if ($PSBoundParameters.ContainsKey('SelfContained') -and -not $SelfContained) { "--self-contained false" } else { "--self-contained true" }
    
    Invoke-Expression "dotnet publish src/CliManager.App/CliManager.App.csproj -c $Configuration -r win-x64 $selfContainedArg -o $outDir --nologo"
    
    if ($LASTEXITCODE -ne 0) {
        Write-Error "发布失败！"
        exit $LASTEXITCODE
    }
    
    Write-Host "✅ 发布完成！产物路径: $outDir" -ForegroundColor Green
} else {
    Write-Host "`n提示: 可使用 .\build.ps1 -Publish 生成独立发布产物。" -ForegroundColor Gray
}
