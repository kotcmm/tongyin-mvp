# Offline-credential-free smoke tests. Invoke from any directory.
$ErrorActionPreference = "Stop"
# Guard against non-ASCII bytes in the PS 5.1 launcher.
$launcherBytes = [System.IO.File]::ReadAllBytes((Join-Path $PSScriptRoot "run-local.ps1"))
$nonAsciiByte = $launcherBytes | Where-Object { $_ -gt 127 } | Select-Object -First 1
if ($null -ne $nonAsciiByte) {
    throw "run-local.ps1 must remain ASCII-only for Windows PowerShell 5.1."
}
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
    Run-Dotnet -Arguments @("build","backend-dotnet/Tongyin.slnx","--configuration","Release")
    Run-Dotnet -Arguments @("run","--project","backend-dotnet/tests/Tongyin.CleanArchitectureChecks/Tongyin.CleanArchitectureChecks.csproj","--configuration","Release")
    Run-Dotnet -Arguments @("run","--project","backend-dotnet/tests/Tongyin.CompatChecks/Tongyin.CompatChecks.csproj","--configuration","Release")
    Run-Dotnet -Arguments @("run","--project","backend-dotnet/tests/Tongyin.ApiChecks/Tongyin.ApiChecks.csproj","--configuration","Release")
    Run-Dotnet -Arguments @("run","--project","backend-dotnet/tests/Tongyin.PayChecks/Tongyin.PayChecks.csproj","--configuration","Release")
    Write-Host ""
    Write-Host "PASS: .NET build, Go compatibility, HTTP flow, simulated signed payment notification."
    Write-Host "Real Seedream, 3D, WeChat payment, and Kuaidi100 API calls are not part of these safe local tests."
} finally {
    Pop-Location
}
