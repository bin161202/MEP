<#
.SYNOPSIS
    Obfuscate tất cả MEPAuto.Client.*.dll trong bin/Release-{ver}/ qua ConfuserEx 2 (mkaring fork).

.DESCRIPTION
    Workflow:
      1. Download ConfuserEx CLI (zip release từ GitHub) cache trong tools/.cache/ConfuserEx-2.x/
      2. Cho mỗi version Revit (input -Versions):
         a. Render template `MEPAuto.Client.crproj` thay {BASE_DIR} {OUTPUT_DIR} {REVIT_DIR}
            + append <module> tags cho mọi MEPAuto.Client.*.dll + MEPAuto.*Feature.dll trong bin folder.
         b. Chạy `Confuser.CLI.exe project.crproj` → output sang bin folder kế bên (Release-{ver}-obfuscated/).
         c. Copy file đã obfuscate ĐÈ lên bin/Release-{ver}/ (in-place, để Build-MSI.ps1 harvest tiếp).
      3. Skip version không có bin folder (build fail trước đó).

    LƯU Ý — ConfuserEx 2 và .NET 8:
    - mkaring/ConfuserEx tốt với net48 (Revit 2022-2024).
    - Net8 (Revit 2025-2027): có thể fail invalid metadata. Script sẽ continue-on-error per version,
      log fail nhưng không throw → MSI vẫn build được (DLL net8 ship un-obfuscated nếu fail).
    - Khi có user net8 thực, đánh giá lại sang Eziriz hoặc Babel (commercial).

.PARAMETER Versions
    Danh sách version Revit cần obfuscate. Default: 2022-2027.

.PARAMETER ConfuserVersion
    Tag GitHub của mkaring/ConfuserEx release. Default: v1.6.0.

.PARAMETER SkipDownload
    Bỏ qua step download (giả định Confuser.CLI.exe đã cache).

.EXAMPLE
    ./tools/obfuscation/Run-Obfuscate.ps1 -Versions 2024
    # Obfuscate chỉ Revit 2024 bin folder.
#>

param(
    [string[]]$Versions = @("2022", "2023", "2024", "2025", "2026", "2027"),
    [string]$RepoRoot = (Resolve-Path "$PSScriptRoot/../..").Path,
    [string]$ConfuserVersion = "v1.6.0",
    [switch]$SkipDownload
)

$ErrorActionPreference = 'Stop'
$cacheDir = Join-Path $RepoRoot "tools/.cache"
$confuserDir = Join-Path $cacheDir "ConfuserEx-$ConfuserVersion"
$confuserCli = Join-Path $confuserDir "Confuser.CLI.exe"
# crproj template (`MEPAuto.Client.crproj` cùng folder) chỉ để IDE preview + reference schema —
# script generate XML inline trong New-ConfuserProject, không đọc template file.

# ==== Step 1: Download ConfuserEx CLI ====
if (-not $SkipDownload -and -not (Test-Path $confuserCli)) {
    Write-Host "==> Download ConfuserEx $ConfuserVersion (mkaring fork)" -ForegroundColor Cyan
    New-Item -ItemType Directory -Force -Path $cacheDir | Out-Null
    $zipUrl = "https://github.com/mkaring/ConfuserEx/releases/download/$ConfuserVersion/ConfuserEx-CLI.zip"
    $zipPath = Join-Path $cacheDir "ConfuserEx-CLI-$ConfuserVersion.zip"
    Invoke-WebRequest -Uri $zipUrl -OutFile $zipPath -UseBasicParsing
    New-Item -ItemType Directory -Force -Path $confuserDir | Out-Null
    Expand-Archive -Path $zipPath -DestinationPath $confuserDir -Force
    Remove-Item $zipPath -Force
}
if (-not (Test-Path $confuserCli)) {
    throw "Confuser.CLI.exe không tìm thấy ở $confuserCli. Check URL release hoặc download thủ công."
}

# ==== Helper: render crproj cho 1 version ====
# Schema mkaring strict: <rule> PHẢI trong <module>, không cho ở <project> root.
# Function sinh full XML inline — KHÔNG dùng template file (template chỉ để reference).
function New-ConfuserProject {
    param(
        [string]$BinDir,
        [string]$OutputDir,
        [string]$RevitVersion,
        [string]$ProjectPath  # output crproj
    )

    # Tìm DLL MEPAuto.Client.*.dll + MEPAuto.*.dll (feature + Common + Shell). KHÔNG đưa Contracts vào
    # vì đã có rule namespace whitelist; nhưng vẫn cần module để rename ctrl flow protection chạy được.
    $dllPattern = @("MEPAuto.Client.*.dll", "MEPAuto.HelloWorld.dll")
    $dlls = @()
    foreach ($p in $dllPattern) {
        $dlls += Get-ChildItem -Path $BinDir -Filter $p -ErrorAction SilentlyContinue
    }
    $dlls = $dlls | Sort-Object FullName -Unique

    if ($dlls.Count -eq 0) {
        throw "Không tìm thấy DLL MEPAuto.* trong $BinDir để obfuscate."
    }

    # Revit dir để probe — ưu tiên cài thật, fallback stubs (CI runner).
    $revitInstall = "C:\Program Files\Autodesk\Revit $RevitVersion"
    $revitStubs = Join-Path $RepoRoot "tools/revit-stubs/$RevitVersion"
    $revitDir = if (Test-Path $revitInstall) { $revitInstall } else { $revitStubs }

    # Common rules block — copy vào MỖI module (inherit="true" → áp dụng cho cả children).
    $rulesXml = @'
    <rule pattern="true" preset="normal" inherit="false">
      <protection id="rename" />
      <protection id="ctrl flow" />
      <protection id="ref proxy" />
      <protection id="constants" />
      <protection id="invalid metadata" />
    </rule>
    <rule pattern="inherits('Autodesk.Revit.UI.IExternalApplication')" inherit="true">
      <protection id="rename" action="remove" />
    </rule>
    <rule pattern="inherits('Autodesk.Revit.UI.IExternalCommand')" inherit="true">
      <protection id="rename" action="remove" />
    </rule>
    <rule pattern="inherits('Autodesk.Revit.UI.IExternalEventHandler')" inherit="true">
      <protection id="rename" action="remove" />
    </rule>
    <rule pattern="namespace('MEPAuto.Contracts')" inherit="true">
      <protection id="rename" action="remove" />
    </rule>
    <rule pattern="is-public() and member-type('property')" inherit="true">
      <protection id="rename" action="remove" />
    </rule>
'@

    # ConfuserEx XSD sequence STRICT order: rule (0+), packer (0-1), module (0+), probePath (0+), plugin (0+).
    # Đặt module TRƯỚC probePath, không thì parser fail "invalid child element".
    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.AppendLine('<?xml version="1.0" encoding="utf-8"?>')
    [void]$sb.AppendLine("<project outputDir=`"$OutputDir`" baseDir=`"$BinDir`" xmlns=`"http://confuser.codeplex.com`">")
    foreach ($dll in $dlls) {
        [void]$sb.AppendLine("  <module path=`"$($dll.Name)`">")
        [void]$sb.AppendLine($rulesXml)
        [void]$sb.AppendLine("  </module>")
    }
    [void]$sb.AppendLine("  <probePath>$BinDir</probePath>")
    [void]$sb.AppendLine("  <probePath>$revitDir</probePath>")
    [void]$sb.AppendLine('</project>')

    Set-Content -Path $ProjectPath -Value $sb.ToString() -Encoding UTF8
}

# ==== Step 2: Obfuscate per version ====
$failures = @()
foreach ($v in $Versions) {
    $binDir = Join-Path $RepoRoot "src/client/MEPAuto.Client.Shell/bin/Release-$v"
    if (-not (Test-Path (Join-Path $binDir "MEPAuto.Client.Shell.dll"))) {
        Write-Host "==> Skip Release-$v : bin folder không có (build fail trước đó)" -ForegroundColor DarkGray
        continue
    }

    $outDir = Join-Path $RepoRoot "src/client/MEPAuto.Client.Shell/bin/Release-$v-obfuscated"
    Remove-Item -Path $outDir -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null

    $crprojPath = Join-Path $env:TEMP "MEPAuto-obfuscate-$v.crproj"
    New-ConfuserProject -BinDir $binDir -OutputDir $outDir -RevitVersion $v -ProjectPath $crprojPath

    Write-Host "==> Obfuscate Release-$v" -ForegroundColor Cyan
    $confProc = Start-Process $confuserCli -ArgumentList @("-n", $crprojPath) -Wait -PassThru -NoNewWindow
    if ($confProc.ExitCode -ne 0) {
        Write-Host "    FAIL Release-$v exit=$($confProc.ExitCode). DLL gốc giữ nguyên (un-obfuscated)." -ForegroundColor Yellow
        $failures += $v
        continue
    }

    # Copy đè kết quả lên bin folder gốc → harvest tiếp dùng version đã obfuscate.
    $obfuscatedDlls = Get-ChildItem -Path $outDir -Filter "MEPAuto.*.dll" -File
    foreach ($dll in $obfuscatedDlls) {
        Copy-Item -Path $dll.FullName -Destination (Join-Path $binDir $dll.Name) -Force
    }
    Write-Host "    Done Release-$v ($($obfuscatedDlls.Count) DLL)" -ForegroundColor Green
}

if ($failures.Count -gt 0) {
    Write-Host ""
    Write-Host "WARN: Obfuscation fail cho version: $($failures -join ', '). MSI sẽ ship DLL un-obfuscated cho version đó." -ForegroundColor Yellow
} else {
    Write-Host ""
    Write-Host "DONE — Obfuscation pass cho tất cả $($Versions.Count) version." -ForegroundColor Green
}
