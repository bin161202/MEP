<#
.SYNOPSIS
    Update /var/mepauto-data/version.json trên VPS với latest version + SHA256 từ GitHub Release.

.DESCRIPTION
    Workflow tự động hoá step "bump VPS version.json" sau mỗi release:
    1. Fetch SHA256 từ GitHub Release asset (yêu cầu git credential helper có PAT)
    2. SSH VPS download SHA256 + cập nhật version.json
    3. Append releaseNotes (fetch từ release body)

    Sau khi chạy, VersionService cache 60s rồi user mở Revit thấy notification (nếu version cũ).

.PARAMETER Version
    Version mới (without 'v' prefix). VD: 0.1.0-rc3.

.PARAMETER MinSupported
    Version tối thiểu hỗ trợ. Default giữ nguyên (0.0.0). Set lớn hơn current để mandatory upgrade.

.PARAMETER VpsHost
    SSH target. Default root@129.212.230.159.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools/deploy/update-version-json.ps1 -Version 0.1.0-rc3
#>

param(
    [Parameter(Mandatory)] [string]$Version,
    [string]$MinSupported,
    [string]$VpsHost = "root@129.212.230.159",
    [string]$Repo = "MEP-Automation/MEPAuto",
    [string]$DataDir = "/var/mepauto-data"
)

$ErrorActionPreference = 'Stop'

# Lấy PAT từ git credential helper
$bashCandidates = @(
    "C:\Program Files\Git\bin\bash.exe",
    "C:\Program Files (x86)\Git\bin\bash.exe"
)
$bash = $bashCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $bash) { throw "Không tìm thấy bash.exe (Git for Windows). Cài Git for Windows hoặc đặt token qua env var GH_TOKEN." }

$cred = & $bash -c "printf 'host=github.com\nprotocol=https\n\n' | git credential fill 2>/dev/null"
$token = ([regex]::Match(($cred -join "`n"), '^password=(.+)$', 'Multiline')).Groups[1].Value.Trim()
if (-not $token) { throw "Không lấy được GitHub PAT. Chạy ``git push`` 1 lần để cache credential." }

Write-Host "==> Fetch release v$Version metadata + SHA256 từ GitHub API" -ForegroundColor Cyan
$releaseUrl = "https://api.github.com/repos/$Repo/releases/tags/v$Version"
$release = Invoke-RestMethod -Uri $releaseUrl -Headers @{ Authorization = "Bearer $token"; Accept = "application/vnd.github+json" }
$shaAsset = $release.assets | Where-Object { $_.name -eq "MEPAuto-Setup-v$Version.msi.sha256" }
if (-not $shaAsset) { throw "Không tìm thấy asset SHA256 cho v$Version. Release đã build chưa?" }

$shaContent = Invoke-RestMethod -Uri $shaAsset.url -Headers @{ Authorization = "Bearer $token"; Accept = "application/octet-stream" }
$sha256 = ($shaContent -split '\s+')[0].ToLower()
if ($sha256 -notmatch '^[0-9a-f]{64}$') { throw "SHA256 invalid: $sha256" }
Write-Host "    SHA256: $sha256" -ForegroundColor Green

$versionJson = @{
    latest = $Version
    minSupported = if ($MinSupported) { $MinSupported } else { "0.0.0" }
    downloadUrlPattern = "https://github.com/$Repo/releases/download/v{version}/MEPAuto-Setup-v{version}.msi"
    sha256ByVersion = @{ $Version = $sha256 }
    releaseNotes = $release.body
    revitVersions = @("2022", "2023", "2024", "2025", "2026", "2027")
} | ConvertTo-Json -Depth 5

Write-Host "==> Update $DataDir/version.json trên VPS" -ForegroundColor Cyan
$tempLocal = Join-Path $env:TEMP "mepauto-version-$Version.json"
$versionJson | Set-Content -Path $tempLocal -Encoding UTF8

& scp $tempLocal "$VpsHost`:$DataDir/version.json"
if ($LASTEXITCODE -ne 0) { throw "scp fail" }
& ssh $VpsHost "chown 1000:1000 $DataDir/version.json && chmod 644 $DataDir/version.json && echo 'OK' && cat $DataDir/version.json"
Remove-Item $tempLocal -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "DONE — VPS version.json updated to v$Version." -ForegroundColor Green
Write-Host "Cache 60s → user mở Revit (chạy version cũ hơn) sẽ thấy update notification trong 90s." -ForegroundColor White
