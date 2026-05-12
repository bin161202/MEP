<#
.SYNOPSIS
    Verify + install RevitAddinManager (chuongmep/RevitAddInManager) cho mỗi version Revit cài trên máy dev.

.DESCRIPTION
    RevitAddinManager là tool open-source cho phép reload DLL không restart Revit
    (loop dev: build → click "Add-In Manager" → Reload → click feature button → 5-10s).

    Script idempotent:
    1. Loop version Revit 2022-2027
    2. Skip nếu Revit version đó không cài
    3. Skip nếu .addin RAM đã có ở %AppData%\Autodesk\Revit\Addins\<ver>\
    4. Nếu chưa có và đã có RAM ở 1 version khác → copy sang
    5. Nếu chưa có ở version nào → in hướng dẫn manual download

.PARAMETER Versions
    Default: @("2022","2023","2024","2025","2026","2027").

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools/dev-setup/install-revit-addin-manager.ps1
    powershell -ExecutionPolicy Bypass -File tools/dev-setup/install-revit-addin-manager.ps1 -Versions @("2024","2025")

.NOTES
    Reference: https://github.com/chuongmep/RevitAddInManager
#>

param(
    [string[]]$Versions = @("2022", "2023", "2024", "2025", "2026", "2027"),
    [string]$RevitInstallRoot = "C:\Program Files\Autodesk"
)

$ErrorActionPreference = 'Stop'
$addinsRoot = Join-Path $env:AppData "Autodesk\Revit\Addins"

function Test-RevitInstalled {
    param([string]$Version)
    return Test-Path (Join-Path $RevitInstallRoot "Revit $Version\RevitAPI.dll")
}

function Test-RamInstalled {
    param([string]$Version)
    $addin = Join-Path $addinsRoot "$Version\RevitAddinManager.addin"
    $folder = Join-Path $addinsRoot "$Version\RevitAddinManager"
    return (Test-Path $addin) -and (Test-Path $folder)
}

# ===== Pass 1: discover state =====
$state = @()
foreach ($v in $Versions) {
    $hasRevit = Test-RevitInstalled $v
    $hasRam = Test-RamInstalled $v
    $state += [PSCustomObject]@{
        Version  = $v
        HasRevit = $hasRevit
        HasRam   = $hasRam
    }
}

Write-Host "==> Trạng thái" -ForegroundColor Cyan
$state | Format-Table -AutoSize | Out-String | Write-Host

# Source for copy: any version có cả Revit + RAM
$sourceVersion = ($state | Where-Object { $_.HasRevit -and $_.HasRam } | Select-Object -First 1).Version

# ===== Pass 2: action =====
$installed = 0
$skipped = 0
$missing = @()

foreach ($s in $state) {
    if (-not $s.HasRevit) {
        Write-Host "[SKIP] Revit $($s.Version) chưa cài — bỏ qua" -ForegroundColor DarkGray
        $skipped++
        continue
    }
    if ($s.HasRam) {
        Write-Host "[OK]   Revit $($s.Version) đã có RAM, skip" -ForegroundColor Green
        $skipped++
        continue
    }

    # Need to install RAM cho version này
    if ($sourceVersion) {
        Write-Host "[COPY] Copy RAM từ Revit $sourceVersion → Revit $($s.Version)..." -ForegroundColor Yellow
        $sourceAddin = Join-Path $addinsRoot "$sourceVersion\RevitAddinManager.addin"
        $sourceFolder = Join-Path $addinsRoot "$sourceVersion\RevitAddinManager"
        $destAddin = Join-Path $addinsRoot "$($s.Version)\RevitAddinManager.addin"
        $destFolder = Join-Path $addinsRoot "$($s.Version)\RevitAddinManager"

        New-Item -ItemType Directory -Force -Path (Split-Path $destAddin) | Out-Null
        Copy-Item $sourceAddin $destAddin -Force
        Copy-Item $sourceFolder $destFolder -Recurse -Force

        if ((Test-RamInstalled $s.Version)) {
            Write-Host "       OK installed cho Revit $($s.Version)" -ForegroundColor Green
            $installed++
        } else {
            Write-Host "       FAIL — file copy nhưng verify lại không thấy" -ForegroundColor Red
            $missing += $s.Version
        }
    } else {
        $missing += $s.Version
    }
}

# ===== Summary =====
Write-Host ""
Write-Host "==> Summary" -ForegroundColor Cyan
Write-Host "  Installed: $installed"
Write-Host "  Skipped:   $skipped"
if ($missing.Count -gt 0) {
    Write-Host "  Missing:   $($missing -join ', ')" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Manual install cho các version chưa có:" -ForegroundColor Yellow
    Write-Host "  1. Download release latest ở: https://github.com/chuongmep/RevitAddInManager/releases"
    Write-Host "  2. Extract zip → 1 folder 'RevitAddinManager' chứa DLL + 1 file 'RevitAddinManager.addin'"
    Write-Host "  3. Copy cả 2 vào: $addinsRoot\<version>\"
    Write-Host "  4. Run lại script này để verify"
    Write-Host ""
    Write-Host "  Hoặc: cài manually cho 1 version (vd 2024) → script sẽ tự copy sang version khác."
}
