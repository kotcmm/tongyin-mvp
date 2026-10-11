# Windows PowerShell: run the local backend with variables from backend/.env
# Default development port is 18080; use -Port 18081 to override it.
param(
    [ValidateRange(1, 65535)]
    [int]$Port = 18080
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$backend = Join-Path $root "backend"
$envFile = Join-Path $backend ".env"
if (-not (Test-Path $envFile)) {
    throw "Please copy backend/.env.example to backend/.env and configure database credentials first."
}
foreach ($line in Get-Content $envFile) {
    $trimmed = $line.Trim()
    if ($trimmed -eq "" -or $trimmed.StartsWith("#")) { continue }
    $match = [regex]::Match($line, '^\s*([A-Za-z_][A-Za-z0-9_]*)=(.*)$')
    if (-not $match.Success) { continue }
    $name = $match.Groups[1].Value
    $value = $match.Groups[2].Value.Trim()
    # Keep DSNs intact; don't interpolate strings or execute shell expressions.
    [Environment]::SetEnvironmentVariable($name, $value, "Process")
}
# Dev port must override stale values in an existing ignored backend/.env.
# Both settings must match so signed image/GLB URLs use the running service.
[Environment]::SetEnvironmentVariable("LISTEN_ADDR", ":$Port", "Process")
[Environment]::SetEnvironmentVariable("PUBLIC_URL", "http://localhost:$Port", "Process")
Write-Host "Tongyin local backend: http://localhost:$Port"
Write-Host "Tongyin admin: http://localhost:$Port/admin/"
Push-Location $backend
try {
    go mod tidy
    if ($LASTEXITCODE -ne 0) { throw "go mod tidy failed" }
    go run .
} finally {
    Pop-Location
}
