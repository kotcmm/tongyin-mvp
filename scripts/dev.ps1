# Tongyin development server (Windows PowerShell 5.1+).
# Local emulator: .\scripts\dev.ps1
# Real phone on the same Wi-Fi/LAN: .\scripts\dev.ps1 -Phone
# If more than one adapter: .\scripts\dev.ps1 -Phone -LanIP 192.168.1.100
param(
    [ValidateRange(1, 65535)]
    [int]$Port = 18080,
    [switch]$Phone,
    [string]$LanIP = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$backend = Join-Path $root "backend"
$configFile = Join-Path $root "miniprogram\config.js"
$envFile = Join-Path $backend ".env"

if (-not (Test-Path $envFile)) {
    throw "请先从 backend/.env.example 复制出 backend/.env 并配置数据库/密钥。"
}
if (-not (Test-Path $configFile)) {
    throw "小程序 config.js 不存在：$configFile"
}

# Only private network IPv4 addresses are allowed for the phone development mode.
function Test-PrivateIPv4([string]$address) {
    $parsed = $null
    if (-not [System.Net.IPAddress]::TryParse($address, [ref]$parsed)) { return $false }
    if ($parsed.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork) { return $false }
    $bytes = $parsed.GetAddressBytes()
    if ($bytes[0] -eq 10) { return $true }
    if ($bytes[0] -eq 192 -and $bytes[1] -eq 168) { return $true }
    if ($bytes[0] -eq 172 -and $bytes[1] -ge 16 -and $bytes[1] -le 31) { return $true }
    return $false
}

function Get-PhoneIP {
    if ($LanIP) {
        if (-not (Test-PrivateIPv4 $LanIP)) {
            throw "-LanIP 必须是本机的局域网 IPv4（10.x、172.16-31.x 或 192.168.x）。"
        }
        $ipMatches = @(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
            Where-Object { $_.IPAddress -eq $LanIP })
        if ($ipMatches.Count -eq 0) {
            throw "IP $LanIP 不属于这台电脑。请通过 ipconfig 检查后重试。"
        }
        return $LanIP
    }
    $available = @()
    $routes = @(Get-NetRoute -AddressFamily IPv4 -DestinationPrefix "0.0.0.0/0" -ErrorAction SilentlyContinue |
        Sort-Object RouteMetric)
    foreach ($route in $routes) {
        $adapter = Get-NetAdapter -InterfaceIndex $route.InterfaceIndex -ErrorAction SilentlyContinue
        if (-not $adapter -or $adapter.Status -ne "Up") { continue }
        if ($adapter.Name -match "VPN|Tailscale|WireGuard|vEthernet|Virtual|WSL|Docker|Hyper-V|Loopback|代理") { continue }
        $addresses = @(Get-NetIPAddress -AddressFamily IPv4 -InterfaceIndex $route.InterfaceIndex -ErrorAction SilentlyContinue)
        foreach ($item in $addresses) {
            if ((Test-PrivateIPv4 $item.IPAddress) -and ($available -notcontains $item.IPAddress)) {
                $available += $item.IPAddress
            }
        }
    }
    if ($available.Count -eq 1) { return $available[0] }
    if ($available.Count -eq 0) {
        throw "没有找到可用的局域网 IP。请连接 Wi-Fi/有线网络，运行 ipconfig，再使用 -Phone -LanIP 192.168.x.x。"
    }
    throw ("发现多个可用局域网 IP：" + ($available -join ", ") +
        "；请使用 -Phone -LanIP 具体IP，避免选错网卡。")
}

if ($Phone) {
    $hostIP = Get-PhoneIP
    $baseUrl = "http://${hostIP}:$Port"
} else {
    if ($LanIP) { throw "使用 -LanIP 时必须同时指定 -Phone。" }
    $baseUrl = "http://localhost:$Port"
}

# All signed image/GLB links must use the very same origin as the mini-program
# API_BASE. Otherwise the 3D viewer can receive inaccessible localhost URLs.
foreach ($line in Get-Content $envFile) {
    $trimmed = $line.Trim()
    if ($trimmed -eq "" -or $trimmed.StartsWith("#")) { continue }
    $match = [regex]::Match($line, '^\s*([A-Za-z_][A-Za-z0-9_]*)=(.*)$')
    if (-not $match.Success) { continue }
    [Environment]::SetEnvironmentVariable($match.Groups[1].Value, $match.Groups[2].Value.Trim(), "Process")
}
[Environment]::SetEnvironmentVariable("LISTEN_ADDR", ":$Port", "Process")
[Environment]::SetEnvironmentVariable("PUBLIC_URL", $baseUrl, "Process")

# Temporarily set the mini-program's API address while the backend is running.
# Restore the EXACT original file on shutdown to avoid dirtying tracked code.
# The developer tool must recompile while this command is still running.
$original = [System.IO.File]::ReadAllText($configFile)
$apiPattern = "(?m)(API_BASE\s*:\s*['\x22])https?://[^'\x22]+(['\x22])"
if (-not [regex]::IsMatch($original, $apiPattern)) {
    throw "无法定位 config.js 里的 API_BASE；请检查该文件是否已被手动修改。"
}
$replacement = ('$1' + $baseUrl + '$2')
$modified = [regex]::Replace($original, $apiPattern, $replacement)
$wasChanged = $original -cne $modified

try {
    if ($wasChanged) {
        [System.IO.File]::WriteAllText($configFile, $modified, (New-Object System.Text.UTF8Encoding($false)))
    }
    Write-Host ""
    if ($Phone) {
        Write-Host "===== 童印 · 微信真机调试模式 =====" -ForegroundColor Green
        Write-Host "电脑/手机必须连接同一个局域网（同 Wi-Fi，不要使用访客网络）。"
        Write-Host "手机浏览器先访问：$baseUrl/health"
        Write-Host "小程序 API、图片、GLB 下载链接均已改为：$baseUrl"
        Write-Host "下一步：在后端运行期间，回微信开发者工具【重新编译 → 真机调试】。"
        Write-Host "如果手机浏览器打不开 /health，请先检查 Windows 防火墙的专用网络入站规则（TCP $Port）。"
        Write-Host "如果小程序提示合法域名错误，请在真机调试中开启调试模式；正式版必须使用 HTTPS 合法域名。"
        Write-Host "注意：DEV_LOGIN 共用测试身份，只能在可信局域网中联调，禁止公网暴露。"
    } else {
        Write-Host "===== 童印 · 电脑模拟器模式 =====" -ForegroundColor Green
        Write-Host "如需真机联调，停止当前服务后改用：.\scripts\dev.ps1 -Phone"
    }
    Write-Host "后端 / 管理后台：$baseUrl  /  $baseUrl/admin/"
    Write-Host "停止服务后会恢复小程序 config.js 原内容。"
    Write-Host ""
    Push-Location $backend
    try {
        go mod tidy
        if ($LASTEXITCODE -ne 0) { throw "go mod tidy failed" }
        go run .
        if ($LASTEXITCODE -ne 0) { throw "Go backend startup failed" }
    } finally {
        Pop-Location
    }
} finally {
    if ($wasChanged) {
        [System.IO.File]::WriteAllText($configFile, $original, (New-Object System.Text.UTF8Encoding($false)))
        Write-Host "已恢复 miniprogram/config.js 原内容。" -ForegroundColor DarkGray
    }
}
