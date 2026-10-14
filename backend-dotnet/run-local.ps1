# Start an isolated .NET 10 test instance. Never touches backend/ (Go) data.
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
    Write-Host "首次启动，本地管理员（密码只显示本次，请保存）："
    Write-Host ("账号: " + "admin")
    Write-Host ("密码: " + $env:ADMIN_BOOTSTRAP_PASSWORD)
    Write-Host ""
}
$env:DEV_MODE = "true"
$env:DB_DRIVER = "file"
$env:DATA_DIR = $dataDir
$env:LISTEN_ADDR = ":18081"
$env:PUBLIC_URL = "http://localhost:18081"
$env:ADMIN_BOOTSTRAP_USER = "admin"
$env:ADMIN_TOKEN = ""
Write-Host "准备启动 .NET 10（http://localhost:18081/admin/），使用隔离的本地测试数据目录 $dataDir"
Write-Host "Go 服务器端口和生产数据库不会被此脚本修改。按 Ctrl+C 停止。"
dotnet run --project $project
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
