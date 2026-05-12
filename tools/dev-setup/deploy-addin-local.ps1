<#
.SYNOPSIS
    Deploy MEPAuto add-in cho Revit local dev — copy DLL bundle + patch .addin manifest.

.DESCRIPTION
    Sau khi build MEPAuto.sln xong, script này deploy add-in để Revit auto-load:
      1. Copy *.dll + *.pdb từ src/client/MEPAuto.Client.Shell/bin/Debug-{Ver}/
         vào %LocalAppData%\MEPAuto\{Ver}\
      2. Patch .addin manifest từ installer/addin-manifests/MEPAuto-{Ver}.addin
         (replace __MEPAUTO_INSTALL_PATH__ → path MEPAuto.Client.Shell.dll thực tế)
         → copy vào %AppData%\Autodesk\Revit\Addins\{Ver}\
      3. Patch %LocalAppData%\MEPAuto\config.json → trỏ ServerBaseUrl tới local server
         (mặc định http://localhost:5000 — chạy bootstrap-local-server.ps1 trước).

    Idempotent: chạy nhiều lần OK, ghi đè DLL/.addin cũ.
    KHÔNG cần admin (deploy per-user vào %LocalAppData% + %AppData%).

.PARAMETER RevitVersion
    Default 2025. Hỗ trợ 2022-2027.

.PARAMETER Configuration
    Default Debug-2025. Phải khớp RevitVersion (Debug-2024 cho Revit 2024, etc).

.PARAMETER ServerBaseUrl
    Default http://localhost:5000 — trỏ tới server local dev. Đổi nếu dùng VPS thật.

.EXAMPLE
    pwsh tools/dev-setup/deploy-addin-local.ps1
    pwsh tools/dev-setup/deploy-addin-local.ps1 -RevitVersion 2024 -Configuration Debug-2024
    pwsh tools/dev-setup/deploy-addin-local.ps1 -ServerBaseUrl http://129.212.230.159:8081

.NOTES
    Yêu cầu: build sln Debug-{Ver} TRƯỚC khi chạy.
    Sau script, restart Revit (hoặc dùng Add-In Manager Reload).
#>

param(
    [ValidateSet('2022','2023','2024','2025','2026','2027')]
    [string]$RevitVersion = '2025',

    [string]$Configuration = "Debug-$RevitVersion",

    [string]$ServerBaseUrl = 'http://localhost:5000',

    [string]$RepoRoot = (Resolve-Path "$PSScriptRoot/../..").Path
)

$ErrorActionPreference = 'Stop'

$shellBin = Join-Path $RepoRoot "src/client/MEPAuto.Client.Shell/bin/$Configuration"
$manifestTemplate = Join-Path $RepoRoot "installer/addin-manifests/MEPAuto-$RevitVersion.addin"
$installDir = Join-Path $env:LocalAppData "MEPAuto\$RevitVersion"
$addinsDir = Join-Path $env:AppData "Autodesk\Revit\Addins\$RevitVersion"
$configPath = Join-Path $env:LocalAppData "MEPAuto\config.json"

Write-Host "==> MEPAuto deploy-addin-local cho Revit $RevitVersion ($Configuration)" -ForegroundColor Cyan
Write-Host "    Source: $shellBin"
Write-Host "    Install: $installDir"
Write-Host "    Addins:  $addinsDir"
Write-Host ""

# Sanity checks ─────────────────────────────────────────────────────────
if (-not (Test-Path $shellBin)) {
    Write-Host "ERR: Bin folder chưa tồn tại: $shellBin" -ForegroundColor Red
    Write-Host "     Build trước: dotnet build MEPAuto.sln -c $Configuration"
    exit 1
}
if (-not (Test-Path $manifestTemplate)) {
    Write-Host "ERR: Manifest template không tìm thấy: $manifestTemplate" -ForegroundColor Red
    exit 1
}
$shellDll = Join-Path $shellBin "MEPAuto.Client.Shell.dll"
if (-not (Test-Path $shellDll)) {
    Write-Host "ERR: $shellDll không tồn tại — build chưa thành công?" -ForegroundColor Red
    exit 1
}

# 1 ─ Copy DLL bundle ───────────────────────────────────────────────────
Write-Host "[1/3] Copy DLL bundle → $installDir" -ForegroundColor Yellow
New-Item -ItemType Directory -Force -Path $installDir | Out-Null
$copiedCount = 0
foreach ($pattern in @('*.dll', '*.pdb', '*.deps.json')) {
    Get-ChildItem -Path $shellBin -Filter $pattern | ForEach-Object {
        Copy-Item -Path $_.FullName -Destination $installDir -Force
        $copiedCount++
    }
}
Write-Host "      Copied $copiedCount file(s)" -ForegroundColor Green

# 2 ─ Patch + install .addin manifest ──────────────────────────────────
Write-Host "[2/3] Patch .addin manifest → $addinsDir\MEPAuto-$RevitVersion.addin" -ForegroundColor Yellow
$installedShellDll = Join-Path $installDir "MEPAuto.Client.Shell.dll"
$manifestContent = Get-Content -Path $manifestTemplate -Raw
$manifestPatched = $manifestContent -replace '__MEPAUTO_INSTALL_PATH__', [System.Security.SecurityElement]::Escape($installedShellDll)

New-Item -ItemType Directory -Force -Path $addinsDir | Out-Null
$destAddin = Join-Path $addinsDir "MEPAuto-$RevitVersion.addin"
Set-Content -Path $destAddin -Value $manifestPatched -Encoding UTF8
Write-Host "      Wrote $destAddin" -ForegroundColor Green

# 3 ─ Patch ClientConfig ServerBaseUrl ─────────────────────────────────
Write-Host "[3/3] Patch ClientConfig.json → ServerBaseUrl = $ServerBaseUrl" -ForegroundColor Yellow
New-Item -ItemType Directory -Force -Path (Split-Path $configPath) | Out-Null
$config = @{ ServerBaseUrl = $ServerBaseUrl } | ConvertTo-Json -Depth 3
Set-Content -Path $configPath -Value $config -Encoding UTF8
Write-Host "      Wrote $configPath" -ForegroundColor Green

# ─ Summary ─────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "==> Done." -ForegroundColor Cyan
Write-Host ""
Write-Host "Bước tiếp:" -ForegroundColor Cyan
Write-Host "  A. Khởi động server local (terminal khác):"
Write-Host "       pwsh tools/dev-setup/bootstrap-local-server.ps1"
Write-Host "     (Hoặc trỏ -ServerBaseUrl tới VPS thật khi chạy script này.)"
Write-Host ""
Write-Host "  B. Restart Revit $RevitVersion → tab 'MEPAuto' xuất hiện trên ribbon."
Write-Host "     Bấm 'Hello World' để test."
Write-Host ""
Write-Host "  Sau khi sửa code: build lại sln + chạy lại script này (ghi đè DLL)."
Write-Host "  Có Add-In Manager (RAM) thì dùng RAM Reload → không cần restart Revit."