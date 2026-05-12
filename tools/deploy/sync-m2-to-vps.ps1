<#
.SYNOPSIS
    Sync toàn bộ server code + shared contracts MEPAuto lên VPS, sau đó trigger deploy.sh.

.DESCRIPTION
    Sync folder-level (KHÔNG whitelist file): mọi file/folder mới member thêm vào src/server hoặc
    shared sẽ tự động được sync. Có "diff guard" so với git ls-files để cảnh báo nếu có folder
    server-side tracked-by-git nằm ngoài whitelist (phòng trường hợp thêm folder gốc mới).

    Tiền điều kiện:
    - SSH key đã setup tới VPS (root@129.212.230.159) — không cần password.
    - Local đã build sạch sln (`dotnet build MEPAuto.sln -c Release` 0 warning).
    - VPS path /opt/mepauto/ tồn tại + có deploy.sh.
    - git CLI có trong PATH (cho diff guard).

    Script làm 4 việc:
    1. Diff guard: so $paths với git ls-files src/server shared → cảnh báo folder mới ngoài whitelist
    2. SCP từng path lên VPS (folder dùng scp -r, file dùng scp)
    3. Cleanup bin/obj trên VPS (scp -r kéo cả bin/obj local lên)
    4. SSH chạy deploy.sh system + smoke test /health + /api/v1/version/check

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

# Folder-level sync: mọi file mới member thêm dưới các path này tự động được sync.
# Nếu member thêm folder GỐC mới (vd "src/api-v2"), diff guard sẽ cảnh báo + dừng.
$paths = @(
    # Config build chung (file gốc — sync nguyên file)
    "Directory.Packages.props",
    "Directory.Build.props",
    "MEPAuto.sln",
    # Toàn bộ server code (mọi feature/folder mới tự include)
    "src/server",
    # Toàn bộ shared contracts (mọi DTO mới tự include)
    "shared",
    # Deploy config (docker-compose, nginx, deploy.sh)
    "tools/deploy"
)

# Folder server-side cần kiểm tra coverage bởi $paths (diff guard).
$serverScopes = @("src/server", "shared")

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

# Diff guard — so $paths với git ls-files ($serverScopes)
Write-Host "==> Diff guard — so paths với git ls-files" -ForegroundColor Cyan
$gitAvailable = $null -ne (Get-Command git -ErrorAction SilentlyContinue)
if (-not $gitAvailable) {
    Write-Host "    git CLI không có trong PATH — bỏ qua diff guard" -ForegroundColor Yellow
} else {
    $pathsNorm = $paths | ForEach-Object { $_.Replace('\','/') }
    $trackedFiles = & git -C $RepoRoot ls-files -- $serverScopes 2>$null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "    git ls-files fail — bỏ qua diff guard" -ForegroundColor Yellow
    } else {
        $uncovered = @()
        foreach ($f in $trackedFiles) {
            $fNorm = $f.Replace('\','/')
            $covered = $false
            foreach ($w in $pathsNorm) {
                if ($fNorm -eq $w -or $fNorm.StartsWith("$w/")) {
                    $covered = $true
                    break
                }
            }
            if (-not $covered) { $uncovered += $fNorm }
        }
        if ($uncovered.Count -gt 0) {
            $uncoveredRoots = $uncovered |
                ForEach-Object { ($_ -split '/')[0..1] -join '/' } |
                Sort-Object -Unique
            Write-Host ""
            Write-Host "    CẢNH BÁO: $($uncovered.Count) file tracked-by-git KHÔNG nằm trong `$paths:" -ForegroundColor Yellow
            $uncoveredRoots | ForEach-Object { Write-Host "      - $_/..." -ForegroundColor Yellow }
            Write-Host ""
            Write-Host "    Cần thêm vào `$paths trong sync-m2-to-vps.ps1 trước khi sync." -ForegroundColor Yellow
            if ($DryRun) {
                Write-Host "    (DryRun — tiếp tục để xem các lệnh khác)" -ForegroundColor DarkGray
            } else {
                $ans = Read-Host "    Tiếp tục sync mà KHÔNG có những file trên? (y/N)"
                if ($ans -ne 'y') {
                    Write-Host "Aborted." -ForegroundColor Red
                    exit 1
                }
            }
        } else {
            Write-Host "    OK — $($trackedFiles.Count) file server-side đều được cover bởi `$paths" -ForegroundColor Green
        }
    }
}

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

# Cleanup bin/obj trên VPS (scp -r kéo cả bin/obj local lên — DLL Windows-built không dùng được trong image Linux + tốn dung lượng)
Invoke-Cmd "ssh $VpsHost `"find $VpsPath/src/server $VpsPath/shared -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} + 2>/dev/null; true`"" "Cleanup bin/obj trên VPS"

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
