<#
.SYNOPSIS
    Helper tạo user đầu tiên trong /var/mepauto-data/users.json (Phase 1 storage).

.DESCRIPTION
    BCrypt hash password (work factor 11) — tương thích server-side BCryptPasswordHasher.
    Output JSON snippet để LEAD copy lên VPS rồi merge vào /var/mepauto-data/users.json.

    KHÔNG kết nối server — chỉ sinh JSON cục bộ.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools/deploy/seed-user.ps1 -Email "lead@yourcompany.com" -DisplayName "LEAD" -Features helloworld.basic

.NOTES
    Yêu cầu BCrypt.Net-Next có sẵn — đã ref bởi project Server, có thể load từ build output.
#>

param(
    [Parameter(Mandatory)] [string]$Email,
    [string]$DisplayName = "",
    [SecureString]$Password,
    [string[]]$Features = @("helloworld.basic"),
    [string]$RepoRoot = (Resolve-Path "$PSScriptRoot/../..").Path
)

$ErrorActionPreference = 'Stop'

# Hỏi password nếu chưa truyền
if (-not $Password) {
    $Password = Read-Host -AsSecureString "Mật khẩu cho $Email"
}
$bstr = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($Password)
$plain = [System.Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
[System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)

# Load BCrypt.Net từ build output server
$bcryptDll = Get-ChildItem -Path (Join-Path $RepoRoot "src/server") -Filter "BCrypt.Net-Next.dll" -Recurse -ErrorAction SilentlyContinue |
    Select-Object -First 1
if (-not $bcryptDll) {
    Write-Host "ERR: BCrypt.Net-Next.dll không tìm thấy. Build server trước:" -ForegroundColor Red
    Write-Host "     dotnet build src/server/MEPAuto.Server.Api/MEPAuto.Server.Api.csproj -c Release"
    exit 1
}
Add-Type -Path $bcryptDll.FullName

$hash = [BCrypt.Net.BCrypt]::HashPassword($plain, 11)
$userId = "u-" + ([guid]::NewGuid().ToString("N").Substring(0, 8))

$userJson = @"
{
  "userId": "$userId",
  "email": "$Email",
  "passwordHash": "$hash",
  "displayName": "$DisplayName",
  "disabled": false,
  "createdAt": "$(([DateTime]::UtcNow).ToString('o'))",
  "lastLoginAt": null
}
"@

$licenseJson = "  `"$Email`": [$($Features | ForEach-Object { "`"$_`"" } | Join-String -Separator ', ')]"

Write-Host ""
Write-Host "===== Append vào /var/mepauto-data/users.json (mảng [...]) =====" -ForegroundColor Green
Write-Host $userJson
Write-Host ""
Write-Host "===== Append vào /var/mepauto-data/licenses.json (object {...}) =====" -ForegroundColor Green
Write-Host $licenseJson
Write-Host ""
Write-Host "Hướng dẫn upload lên VPS:" -ForegroundColor Cyan
Write-Host "  1. SSH vào VPS"
Write-Host "  2. Edit /var/mepauto-data/users.json — paste user object vào array"
Write-Host "  3. Edit /var/mepauto-data/licenses.json — paste line vào object map"
Write-Host "  4. chmod 600 /var/mepauto-data/users.json /var/mepauto-data/licenses.json"
Write-Host "  5. chown 1000:1000 /var/mepauto-data/*"
Write-Host "  6. docker compose -f /opt/mepauto/tools/deploy/docker-compose.system-nginx.yml restart api"
