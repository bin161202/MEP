<#
.SYNOPSIS
    Sinh stub Revit DLL cho CI build (machine không có Revit cài).

.DESCRIPTION
    LEAD chạy 1 lần trên máy có Revit 2022-2027 cài đầy đủ → tạo
    tools/revit-stubs/{version}/{RevitAPI.dll,RevitAPIUI.dll,AdWindows.dll}.

    Stub cần ở repo CI (vd GitHub Actions runner) build pass mà không cần Revit.

    LƯU Ý LICENSE:
    - Autodesk EULA hạn chế redistribute Revit DLL → script này CHỈ dùng cho repo private.
    - Public repo phải dùng metadata-only stub generator (vd JustAssembly, GenAPI).
    - Phase 1: copy nguyên DLL (single-tenant, repo private). Phase sau nâng cấp nếu cần.

.PARAMETER RevitInstallRoot
    Default: C:\Program Files\Autodesk\

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools/build-revit-stubs.ps1
#>

param(
    [string]$RevitInstallRoot = "C:\Program Files\Autodesk",
    [string]$RepoRoot = (Resolve-Path "$PSScriptRoot/..").Path,
    [string[]]$Versions = @("2022", "2023", "2024", "2025", "2026", "2027"),
    [string[]]$Dlls = @("RevitAPI.dll", "RevitAPIUI.dll", "AdWindows.dll")
)

$ErrorActionPreference = 'Stop'
$stubRoot = Join-Path $RepoRoot "tools/revit-stubs"
$missing = @()
$copied = @()

foreach ($version in $Versions) {
    $sourceDir = Join-Path $RevitInstallRoot "Revit $version"
    if (-not (Test-Path $sourceDir)) {
        Write-Host "  SKIP $version : không cài ở $sourceDir" -ForegroundColor DarkGray
        $missing += $version
        continue
    }
    $destDir = Join-Path $stubRoot $version
    New-Item -ItemType Directory -Force -Path $destDir | Out-Null

    foreach ($dll in $Dlls) {
        $src = Join-Path $sourceDir $dll
        $dst = Join-Path $destDir $dll
        if (Test-Path $src) {
            Copy-Item $src $dst -Force
            $copied += "$version/$dll"
            Write-Host "  COPY $version/$dll" -ForegroundColor Green
        } else {
            Write-Host "  WARN $version/$dll : source không tồn tại ($src)" -ForegroundColor Yellow
        }
    }
}

# README cho stub folder
$readme = Join-Path $stubRoot "README.md"
@"
# Revit stubs cho CI build

Folder này chứa Revit reference DLL copy từ máy LEAD (chạy ``tools/build-revit-stubs.ps1``).

**KHÔNG ship MSI installer** (.gitignore loại bin/ → MSI ko gồm). Mục đích: cho GitHub Actions runner
compile được client project mà không cần cài Revit (Revit không có Linux + license phức tạp).

**License**: Autodesk Revit DLL — chỉ commit vào repo private. Public repo phải dùng
metadata-only stub generator (JustAssembly/GenAPI).

Re-generate khi cần: ``powershell -ExecutionPolicy Bypass -File tools/build-revit-stubs.ps1``

Last refresh: $(Get-Date -Format 'yyyy-MM-dd HH:mm')
Versions copied: $($copied -join ', ')
Versions missing on this machine: $($missing -join ', ')
"@ | Set-Content -Path $readme -Encoding UTF8

Write-Host ""
Write-Host "Done. Stub copied: $($copied.Count) DLL."
Write-Host "Missing versions (Revit không cài trên máy này): $($missing -join ', ')"
Write-Host ""
Write-Host "Commit:" -ForegroundColor Cyan
Write-Host "  git add tools/revit-stubs/" -ForegroundColor Cyan
Write-Host "  git commit -m 'chore: refresh Revit stubs'" -ForegroundColor Cyan
