# ASCII-only by design: Windows PowerShell 5.1 decodes UTF-8 without BOM
# using the system ANSI code page and can misparse non-ASCII quoted strings.
# Start a local .NET API with an isolated data directory (never Go's database).
$ErrorActionPreference = "Stop"

$project = Join-Path $PSScriptRoot "src/Tongyin.Api/Tongyin.Api.csproj"
$dataDir = Join-Path $PSScriptRoot "data"
New-Item -ItemType Directory -Force -Path $dataDir | Out-Null
$secretFile = Join-Path $dataDir "local-session-secret.txt"
$dbFile = Join-Path $dataDir "store.json"

function New-LocalSecret([int]$count) {
    $bytes = New-Object byte[] $count
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    return [Convert]::ToBase64String($bytes)
}

if (-not $env:SESSION_SECRET) {
    if (Test-Path $secretFile) {
        $env:SESSION_SECRET = (Get-Content -Raw -Path $secretFile).Trim()
    } else {
        $env:SESSION_SECRET = New-LocalSecret 32
        [System.IO.File]::WriteAllText($secretFile, $env:SESSION_SECRET)
    }
}

if (-not (Test-Path $dbFile) -and -not $env:ADMIN_BOOTSTRAP_PASSWORD) {
    $env:ADMIN_BOOTSTRAP_PASSWORD = New-LocalSecret 18
    Write-Host ""
    Write-Host "FIRST START - save these credentials now:"
    Write-Host "Admin username: admin"
    Write-Host ("Admin password: " + $env:ADMIN_BOOTSTRAP_PASSWORD)
    Write-Host ""
}

$env:DEV_MODE = "true"
$env:DB_DRIVER = "file"
$env:DATA_DIR = $dataDir
$env:LISTEN_ADDR = ":18080"
$env:PUBLIC_URL = "http://localhost:18080"
$env:ADMIN_BOOTSTRAP_USER = "admin"
$env:ADMIN_TOKEN = ""

# Give an actionable error before dotnet tries binding an occupied port.
$portCheck = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Any, 18080)
try {
    $portCheck.Start()
} catch {
    Write-Error "Port 18080 is already in use. Stop the Go or other .NET backend before launching C#."
    exit 1
} finally {
    $portCheck.Stop()
}

Write-Host ("Starting local .NET API at http://localhost:18080/admin/")
Write-Host ("Isolated test data: " + $dataDir)
Write-Host "The Go server and production database are not changed. Ctrl+C to stop."
dotnet run --project $project
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
