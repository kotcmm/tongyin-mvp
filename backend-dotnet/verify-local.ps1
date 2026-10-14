# Offline-credential-free smoke tests. Invoke from any directory.
$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Push-Location $repoRoot
try {
    $commands = @(
        @("build", "backend-dotnet/src/Tongyin.Api/Tongyin.Api.csproj", "--configuration", "Release"),
        @("run", "--project", "backend-dotnet/tests/Tongyin.CompatChecks/Tongyin.CompatChecks.csproj", "--configuration", "Release"),
        @("run", "--project", "backend-dotnet/tests/Tongyin.ApiChecks/Tongyin.ApiChecks.csproj", "--configuration", "Release")
    )
    foreach ($args in $commands) {
        Write-Host ""
        Write-Host ("dotnet " + ($args -join " "))
        & dotnet @args
        if ($LASTEXITCODE -ne 0) { throw "dotnet tests failed with exit code $LASTEXITCODE" }
    }
    Write-Host ""
    Write-Host "PASS：C# 构建、跨语言兼容性用例及 HTTP 端到端本地测试全部通过。"
    Write-Host "注：真实微信支付、Seedream 和快递100 API 未包含在本地模拟测试中。"
} finally {
    Pop-Location
}
