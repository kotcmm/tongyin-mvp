# Offline-credential-free smoke tests. Invoke from any directory.
$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Push-Location $repoRoot
try {
    function Run-Dotnet([string[]]$Arguments) {
        Write-Host ""
        Write-Host ("dotnet " + ($Arguments -join " "))
        & dotnet @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw ("dotnet returned error code " + $LASTEXITCODE)
        }
    }
    Run-Dotnet -Arguments @("build","backend-dotnet/src/Tongyin.Api/Tongyin.Api.csproj","--configuration","Release")
    Run-Dotnet -Arguments @("run","--project","backend-dotnet/tests/Tongyin.CompatChecks/Tongyin.CompatChecks.csproj","--configuration","Release")
    Run-Dotnet -Arguments @("run","--project","backend-dotnet/tests/Tongyin.ApiChecks/Tongyin.ApiChecks.csproj","--configuration","Release")
    Run-Dotnet -Arguments @("run","--project","backend-dotnet/tests/Tongyin.PayChecks/Tongyin.PayChecks.csproj","--configuration","Release")
    Write-Host ""
    Write-Host "PASS: .NET build, Go compatibility, HTTP flow, simulated signed payment notification."
    Write-Host "Real Seedream, 3D, WeChat payment, and Kuaidi100 API calls are not part of these safe local tests."
} finally {
    Pop-Location
}
