<#
.SYNOPSIS
    Build MEPAuto MSI installer end-to-end (build 6 client config + harvest + WiX compile).

.DESCRIPTION
    Workflow:
      1. Build MEPAuto.sln cho 6 config Release-2022..Release-2027
      2. Harvest mỗi bin/Release-{ver}/ → installer/Files_{ver}.wxs (ComponentGroup Id="Files_{ver}")
      3. Build installer/MEPAuto.Installer.wixproj → MEPAuto-Setup.msi

    LƯU Ý:
    - Cần WiX v4 CLI: `dotnet tool install --global wix`

.PARAMETER SkipBuild
    Bỏ qua bước 1 (giả định bin/ đã sẵn).

.PARAMETER Version
    Product version (3 phần X.Y.Z) cho MSI ProductVersion. Default: 0.0.0.

.PARAMETER Validate
    Sau khi build MSI, chạy msiexec install + uninstall silent để verify MSI valid.

.PARAMETER SkipObfuscation
    Bỏ qua step ConfuserEx — build MSI với DLL un-obfuscated (dev local hoặc rollback emergency).

.EXAMPLE
    ./installer/Build-MSI.ps1 -Version 0.1.0
    ./installer/Build-MSI.ps1 -Version 0.1.0 -Validate
    ./installer/Build-MSI.ps1 -Version 0.1.0 -SkipObfuscation
#>

param(
    [switch]$SkipBuild,
    [string]$RepoRoot = (Resolve-Path "$PSScriptRoot/..").Path,
    [string[]]$Versions = @("2022", "2023", "2024", "2025", "2026", "2027"),
    [string]$Version,
    [switch]$Validate,
    [switch]$SkipObfuscation
)

$ErrorActionPreference = 'Stop'
$installerDir = Join-Path $RepoRoot "installer"
$slnPath = Join-Path $RepoRoot "MEPAuto.sln"

# ==== Resolve version ====
if (-not $Version) {
    $versionStampPath = Join-Path $RepoRoot "tools/version-stamp.ps1"
    if (Test-Path $versionStampPath) {
        $info = & $versionStampPath -AsJson | ConvertFrom-Json
        $Version = $info.msi
    } else {
        $Version = "0.0.0"
    }
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version '$Version' không hợp lệ. Format yêu cầu: X.Y.Z (3 phần)."
}
$msiProductVersion = "$Version.0"
Write-Host "==> MSI ProductVersion: $msiProductVersion" -ForegroundColor Cyan

# ==== Step 1: Build client cho từng version ====
$availableVersions = @()
foreach ($v in $Versions) {
    if ($SkipBuild) {
        if (Test-Path (Join-Path $RepoRoot "src/client/MEPAuto.Client.Shell/bin/Release-$v/MEPAuto.Client.Shell.dll")) {
            $availableVersions += $v
        }
        continue
    }
    Write-Host "==> Building Release-$v (Version=$Version)" -ForegroundColor Cyan
    & dotnet build $slnPath -c "Release-$v" --nologo "-p:Version=$Version" 2>&1 | Tee-Object -Variable buildOutput | Out-Host
    if ($LASTEXITCODE -eq 0) {
        $availableVersions += $v
    } else {
        Write-Host "    Skip $v : build fail (Revit có thể chưa cài, sẽ tiếp với version khác)" -ForegroundColor Yellow
    }
}

if ($availableVersions.Count -eq 0) {
    throw "Không có version nào build thành công. Cài Revit hoặc check build log."
}
Write-Host "Available versions: $($availableVersions -join ', ')" -ForegroundColor Green

# ==== Step 1.5: Obfuscate (ConfuserEx 2) ====
if (-not $SkipObfuscation) {
    Write-Host ""
    Write-Host "==> Obfuscate Client DLLs (ConfuserEx 2)" -ForegroundColor Cyan
    $obfuscateScript = Join-Path $RepoRoot "tools/obfuscation/Run-Obfuscate.ps1"
    if (-not (Test-Path $obfuscateScript)) {
        Write-Host "    WARN: $obfuscateScript không tồn tại — skip obfuscation." -ForegroundColor Yellow
    } else {
        & $obfuscateScript -Versions $availableVersions -RepoRoot $RepoRoot
        if ($LASTEXITCODE -ne 0) {
            throw "Obfuscation script throw — abort MSI build."
        }
    }
}

# ==== Step 2: Harvest mỗi bin folder thành Files_{ver}.wxs ====
function New-FilesWxs {
    param(
        [Parameter(Mandatory)] [string]$BinDir,
        [Parameter(Mandatory)] [string]$Version,
        [Parameter(Mandatory)] [string]$OutPath
    )
    $componentGroup = "Files_$Version"
    $directoryRef = "DIR_USER_$Version"
    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.AppendLine('<?xml version="1.0" encoding="UTF-8"?>')
    [void]$sb.AppendLine('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">')
    [void]$sb.AppendLine('  <Fragment>')
    [void]$sb.AppendLine("    <ComponentGroup Id=`"$componentGroup`" Directory=`"$directoryRef`">")

    $files = Get-ChildItem -Path $BinDir -File -Filter '*.dll' -ErrorAction SilentlyContinue
    $files += Get-ChildItem -Path $BinDir -File -Filter '*.pdb' -ErrorAction SilentlyContinue

    foreach ($f in $files) {
        $safeId = ($f.Name -replace '[^A-Za-z0-9]', '_')
        $compId = "C_${Version}_$safeId"
        $fileId = "F_${Version}_$safeId"
        if ($compId.Length -gt 72) { $compId = $compId.Substring(0, 72) }
        if ($fileId.Length -gt 72) { $fileId = $fileId.Substring(0, 72) }
        $src = $f.FullName
        [void]$sb.AppendLine("      <Component Id=`"$compId`" Guid=`"*`">")
        [void]$sb.AppendLine("        <File Id=`"$fileId`" Source=`"$src`" KeyPath=`"yes`" />")
        [void]$sb.AppendLine("      </Component>")
    }

    [void]$sb.AppendLine('    </ComponentGroup>')
    [void]$sb.AppendLine('  </Fragment>')
    [void]$sb.AppendLine('</Wix>')
    Set-Content -Path $OutPath -Value $sb.ToString() -Encoding UTF8
    return $files.Count
}

foreach ($v in $Versions) {
    $binDir = Join-Path $RepoRoot "src/client/MEPAuto.Client.Shell/bin/Release-$v"
    $wxsOut = Join-Path $installerDir "Files_$v.wxs"
    if ($availableVersions -notcontains $v) {
        @"
<?xml version="1.0" encoding="UTF-8"?>
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
  <Fragment>
    <ComponentGroup Id="Files_$v" />
  </Fragment>
</Wix>
"@ | Set-Content -Path $wxsOut -Encoding UTF8
        Write-Host "    Empty harvest cho $v (không build được)." -ForegroundColor DarkGray
        continue
    }

    Write-Host "==> Harvest $binDir → Files_$v.wxs" -ForegroundColor Cyan
    $count = New-FilesWxs -BinDir $binDir -Version $v -OutPath $wxsOut
    Write-Host "    Generated $count file entries trong Files_$v.wxs" -ForegroundColor Green
}

# ==== Step 3: Build MSI ====
Write-Host "==> Build MEPAuto.Installer.wixproj (ProductVersion=$msiProductVersion)" -ForegroundColor Cyan
$wixproj = Join-Path $installerDir "MEPAuto.Installer.wixproj"
& dotnet build $wixproj -c Release --nologo "-p:ProductVersion=$msiProductVersion" 2>&1 | Out-Host
if ($LASTEXITCODE -ne 0) { throw "MSI build fail" }

# Locate output
$msiOut = Get-ChildItem $installerDir -Filter "*.msi" -Recurse | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $msiOut) {
    Write-Host "WARN: MSI build báo OK nhưng không tìm thấy file .msi." -ForegroundColor Yellow
    return
}
Write-Host ""
Write-Host "DONE — MSI output: $($msiOut.FullName)" -ForegroundColor Green

# ==== Step 4 (optional): Validate cài/gỡ ====
if ($Validate) {
    Write-Host ""
    Write-Host "==> Validate: msiexec install/uninstall smoke test" -ForegroundColor Cyan
    $logInstall = Join-Path $env:TEMP "MEPAuto-install.log"
    $logUninstall = Join-Path $env:TEMP "MEPAuto-uninstall.log"

    Write-Host "    msiexec /i $($msiOut.Name) /qn FORCE_ALL_VERSIONS=1 MSIINSTALLPERUSER=1"
    $installProc = Start-Process msiexec -ArgumentList @(
        "/i", "`"$($msiOut.FullName)`"",
        "/qn", "/norestart",
        "/l*v", "`"$logInstall`"",
        "FORCE_ALL_VERSIONS=1",
        "MSIINSTALLPERUSER=1"
    ) -Wait -PassThru -NoNewWindow

    if ($installProc.ExitCode -ne 0 -and $installProc.ExitCode -ne 3010) {
        Write-Host "    Install FAIL exit=$($installProc.ExitCode). Tail log:" -ForegroundColor Red
        Get-Content $logInstall -Tail 40 -ErrorAction SilentlyContinue | ForEach-Object { Write-Host "      $_" -ForegroundColor DarkRed }
        throw "MSI install validation failed"
    }
    Write-Host "    Install OK (exit=$($installProc.ExitCode))" -ForegroundColor Green

    Write-Host "    msiexec /x $($msiOut.Name) /qn"
    $uninstallProc = Start-Process msiexec -ArgumentList @(
        "/x", "`"$($msiOut.FullName)`"",
        "/qn", "/norestart",
        "/l*v", "`"$logUninstall`""
    ) -Wait -PassThru -NoNewWindow

    if ($uninstallProc.ExitCode -ne 0 -and $uninstallProc.ExitCode -ne 3010) {
        Write-Host "    Uninstall FAIL exit=$($uninstallProc.ExitCode). Tail log:" -ForegroundColor Red
        Get-Content $logUninstall -Tail 40 -ErrorAction SilentlyContinue | ForEach-Object { Write-Host "      $_" -ForegroundColor DarkRed }
        throw "MSI uninstall validation failed"
    }
    Write-Host "    Uninstall OK (exit=$($uninstallProc.ExitCode))" -ForegroundColor Green
    Write-Host "Validate: PASS" -ForegroundColor Green
}
