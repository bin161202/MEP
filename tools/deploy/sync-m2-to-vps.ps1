<#
.SYNOPSIS
    Sync các thay đổi M2 (Phase 2 monitoring + Phase 5 versioning) lên VPS MEPAuto.

.DESCRIPTION
    Script này gom tất cả file mới/sửa thành 1 lệnh, tránh quên file.

    Tiền điều kiện:
    - SSH key đã setup tới VPS (root@129.212.230.159) — không cần password.
    - Local đã build sạch sln (`dotnet build MEPAuto.sln -c Release` 0 warning).
    - VPS path /opt/mepauto/ tồn tại + có deploy.sh.

    Script làm 3 việc:
    1. SCP các file/folder thay đổi lên VPS (path tương đối giữ nguyên)
    2. SSH chạy deploy.sh system (rebuild image + restart container)
    3. Smoke test /health + /api/v1/version/check

.PARAMETER VpsHost
    SSH target. Default: root@129.212.230.159.

.PARAMETER VpsPath
    Path remote. Default /opt/mepauto.

.PARAMETER SkipDeploy
    Chỉ sync file, không chạy deploy.sh.

.PARAMETER DryRun
    In ra danh sách lệnh sẽ chạy, không thực hiện.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools/deploy/sync-m2-to-vps.ps1
    powershell -ExecutionPolicy Bypass -File tools/deploy/sync-m2-to-vps.ps1 -DryRun
#>

param(
    [string]$VpsHost = "root@129.212.230.159",
    [string]$VpsPath = "/opt/mepauto",
    [string]$DataDir = "/var/mepauto-data",
    [switch]$SkipDeploy,
    [switch]$DryRun,
    [string]$RepoRoot = (Resolve-Path "$PSScriptRoot/../..").Path
)

$ErrorActionPreference = 'Stop'

$paths = @(
    # Phase 2 — server monitoring
    "Directory.Packages.props",
    "src/server/MEPAuto.Server.Api/MEPAuto.Server.Api.csproj",
    "src/server/MEPAuto.Server.Api/Program.cs",
    "src/server/MEPAuto.Server.Api/Controllers/HealthController.cs",
    "src/server/MEPAuto.Server.Api/Controllers/MetricsController.cs",
    "src/server/MEPAuto.Server.Api/Middleware/CorrelationMiddleware.cs",
    "src/server/MEPAuto.Server.Api/Middleware/MetricsMiddleware.cs",
    # Phase 5 — versioning feature + DTO
    "shared/MEPAuto.Contracts/DTOs/VersionInfoDto.cs",
    "src/server/features/MEPAuto.Server.Versioning"  # toàn folder
)

function Invoke-Cmd {
    param([string]$Command, [string]$Description)
    Write-Host "==> $Description" -ForegroundColor Cyan
    Write-Host "    $Command" -ForegroundColor DarkGray
    if ($DryRun) { return }
    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        Invoke-Expression $Command
        if ($LASTEXITCODE -ne 0) { throw "Command exit $LASTEXITCODE" }
    }
    finally { $ErrorActionPreference = $prev }
}

# Pre-flight check
Write-Host "==> Pre-flight" -ForegroundColor Cyan
foreach ($p in $paths) {
    $local = Join-Path $RepoRoot $p
    if (-not (Test-Path $local)) {
        throw "Path không tồn tại local: $local"
    }
}
Write-Host "    All $($paths.Count) path tồn tại OK" -ForegroundColor Green

# SCP từng path
foreach ($p in $paths) {
    $local = Join-Path $RepoRoot $p
    $pNorm = $p.Replace('\','/')
    $parentDir = if ($pNorm.Contains('/')) { $pNorm -replace '/[^/]*$', '' } else { '' }
    $remoteDir = if ([string]::IsNullOrEmpty($parentDir)) { $VpsPath } else { "$VpsPath/$parentDir" }
    Invoke-Cmd "ssh $VpsHost `"mkdir -p $remoteDir`"" "Ensure remote dir: $remoteDir"

    if (Test-Path $local -PathType Container) {
        Invoke-Cmd "ssh $VpsHost `"rm -rf $VpsPath/$pNorm`"" "Clear remote folder: $pNorm"
        Invoke-Cmd "scp -r `"$local`" $VpsHost`:$remoteDir/" "Sync folder: $p"
    } else {
        Invoke-Cmd "scp `"$local`" $VpsHost`:$VpsPath/$pNorm" "Sync file: $p"
    }
}

# Init version.json template lần đầu nếu chưa có
$versionJsonRemote = "$DataDir/version.json"
$versionExampleRemote = "$VpsPath/tools/deploy/version.json.example"
Invoke-Cmd "scp `"$RepoRoot/tools/deploy/version.json.example`" $VpsHost`:$versionExampleRemote" "Sync version.json.example"
Invoke-Cmd "ssh $VpsHost `"mkdir -p $DataDir && (test -f $versionJsonRemote || (cp $versionExampleRemote $versionJsonRemote && chown 1000:1000 $versionJsonRemote && chmod 644 $versionJsonRemote))`"" "Init version.json nếu chưa có ($versionJsonRemote)"

# Deploy
if (-not $SkipDeploy) {
    Invoke-Cmd "ssh $VpsHost `"cd $VpsPath/tools/deploy && bash deploy.sh system`"" "Run deploy.sh system"

    Write-Host ""
    Write-Host "==> Smoke test endpoints (5s warmup)" -ForegroundColor Cyan
    Start-Sleep -Seconds 5
    Invoke-Cmd "ssh $VpsHost `"curl -s http://127.0.0.1:8081/health | head -c 500; echo`"" "GET /health (port 8081)"
    Invoke-Cmd "ssh $VpsHost `"curl -s http://127.0.0.1:8081/api/v1/version/check?current=0.0.0 | head -c 500; echo`"" "GET /api/v1/version/check"
} else {
    Write-Host ""
    Write-Host "Skipped deploy.sh — chạy thủ công khi sẵn sàng:" -ForegroundColor Yellow
    Write-Host "  ssh $VpsHost 'cd $VpsPath/tools/deploy && bash deploy.sh system'" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "DONE — M2 server changes synced." -ForegroundColor Green
Write-Host "Tiếp theo: edit /var/mepauto-data/version.json bằng tay (latest, sha256, releaseNotes)." -ForegroundColor White
