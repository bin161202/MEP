<#
.SYNOPSIS
    Bootstrap server MEPAuto local cho dev: seed user/license + start ASP.NET Core foreground.

.DESCRIPTION
    Server MEPAuto đòi auth JWT + license check + heartbeat — client KHÔNG chạy offline.
    Script này setup môi trường dev local end-to-end:
      1. Build src/server/MEPAuto.Server.Api (nếu chưa).
      2. Seed src/server/MEPAuto.Server.Api/data-dev/users.json — 1 user dev với BCrypt hash.
      3. Seed src/server/MEPAuto.Server.Api/data-dev/licenses.json — cấp tất cả feature license.
      4. Export env var JWT__SIGNING_KEY (dev-only, KHÔNG dùng prod).
      5. dotnet run server foreground, listen http://localhost:5000.

    Server giữ chạy → client Revit bấm button OK → đóng terminal khi xong.
    Idempotent: chạy lại không tạo trùng user (skip nếu email đã tồn tại).

    LƯU Ý: $features chứa danh sách license keys hiện có trong repo. Khi thêm feature MEP mới,
    update list này để dev user được cấp license tự động khi seed.

.PARAMETER Email
    Default dev@mepauto.local.

.PARAMETER Password
    Default dev123 (chỉ dùng cho local — production luôn dùng strong password qua seed-user.ps1).

.PARAMETER Configuration
    Default Debug-2025.

.PARAMETER Url
    Default http://localhost:5000. Đổi nếu cần port khác.

.EXAMPLE
    pwsh tools/dev-setup/bootstrap-local-server.ps1
    pwsh tools/dev-setup/bootstrap-local-server.ps1 -Email me@test.com -Password mypass
#>

param(
    [string]$Email = 'dev@mepauto.local',
    [string]$Password = 'dev123',
    [string]$Configuration = 'Debug-2025',
    [string]$Url = 'http://localhost:5000',
    [string]$RepoRoot = (Resolve-Path "$PSScriptRoot/../..").Path
)

$ErrorActionPreference = 'Stop'

$serverDir = Join-Path $RepoRoot "src/server/MEPAuto.Server.Api"
$serverCsproj = Join-Path $serverDir "MEPAuto.Server.Api.csproj"
$dataDir = Join-Path $serverDir "data-dev"
$usersFile = Join-Path $dataDir "users.json"
$licensesFile = Join-Path $dataDir "licenses.json"

# Tất cả license của các feature đã có trong repo (admin tự thêm khi feature mới).
$features = @(
    'helloworld.basic'
)

Write-Host "==> MEPAuto bootstrap-local-server" -ForegroundColor Cyan
Write-Host "    DataDir:    $dataDir"
Write-Host "    User:       $Email (password '$Password')"
Write-Host "    Listen URL: $Url"
Write-Host ""

# 1 ─ Ensure server build ──────────────────────────────────────────────
$serverBin = Join-Path $serverDir "bin/$Configuration"
if (-not (Test-Path (Join-Path $serverBin 'MEPAuto.Server.Api.dll'))) {
    Write-Host "[1/5] Build server ($Configuration)..." -ForegroundColor Yellow
    & dotnet build $serverCsproj -c $Configuration | Out-Host
    if ($LASTEXITCODE -ne 0) { Write-Host "ERR: build fail." -ForegroundColor Red; exit 1 }
} else {
    Write-Host "[1/5] Server đã build sẵn ($serverBin)" -ForegroundColor Green
}

# 2 ─ Seed user via HashPwd dev tool (net8 console) ────────────────────
# PS 5.1 KHÔNG load được BCrypt.Net-Next.dll target net8.0 qua Add-Type
# (ReflectionTypeLoadException). Delegate hashing sang dotnet net8 process.
Write-Host "[2/5] Seed user $Email..." -ForegroundColor Yellow
$hashPwdDir = Join-Path $RepoRoot 'tools/dev-setup/HashPwd'
# Directory.Build.props set AppendTargetFrameworkToOutputPath=false → output không có /net8.0/ subfolder
$hashPwdDll = Join-Path $hashPwdDir 'bin/Release/HashPwd.dll'
if (-not (Test-Path $hashPwdDll)) {
    Write-Host "      Build HashPwd tool (1 lần)..." -ForegroundColor DarkGray
    & dotnet build $hashPwdDir -c Release | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Host "ERR: HashPwd build fail." -ForegroundColor Red; exit 1 }
}
if (-not (Test-Path $hashPwdDll)) {
    Write-Host "ERR: HashPwd.dll vẫn không thấy ở $hashPwdDll sau build." -ForegroundColor Red
    exit 1
}

New-Item -ItemType Directory -Force -Path $dataDir | Out-Null

# Idempotent: load users hiện tại, append nếu chưa có
$users = @()
if (Test-Path $usersFile) {
    try { $users = @(Get-Content $usersFile -Raw | ConvertFrom-Json) } catch { $users = @() }
}
$existing = $users | Where-Object { $_.email -eq $Email }
if (-not $existing) {
    $hash = (& dotnet $hashPwdDll $Password).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($hash)) {
        Write-Host "ERR: HashPwd fail (exit=$LASTEXITCODE)." -ForegroundColor Red
        exit 1
    }
    $newUser = [PSCustomObject]@{
        userId = 'u-' + ([guid]::NewGuid().ToString('N').Substring(0, 8))
        email = $Email
        passwordHash = $hash
        displayName = 'Dev User'
        disabled = $false
        createdAt = ([DateTime]::UtcNow).ToString('o')
        lastLoginAt = $null
    }
    $users += $newUser
    # PS 5.1 ConvertTo-Json bug: single-element list unwrap thành object, mất "[]".
    # Server JsonFileUserRepository expect List<User> = JSON array → wrap explicit qua -InputObject @($users).
    ConvertTo-Json -InputObject @($users) -Depth 5 | Set-Content -Path $usersFile -Encoding UTF8
    Write-Host "      Tạo mới user $Email (id=$($newUser.userId))" -ForegroundColor Green
} else {
    Write-Host "      User $Email đã tồn tại, skip" -ForegroundColor DarkGray
}

# 3 ─ Seed licenses (overwrite full feature list cho dev user) ─────────
Write-Host "[3/5] Seed licenses $($features.Count) features..." -ForegroundColor Yellow
$licenses = @{}
if (Test-Path $licensesFile) {
    try { $licenses = Get-Content $licensesFile -Raw | ConvertFrom-Json -AsHashtable } catch { $licenses = @{} }
}
$licenses[$Email] = $features
$licenses | ConvertTo-Json -Depth 5 | Set-Content -Path $licensesFile -Encoding UTF8
Write-Host "      $Email → $($features -join ', ')" -ForegroundColor Green

# 4 ─ Set env vars (dev-only key) ──────────────────────────────────────
Write-Host "[4/5] Set env vars cho process server" -ForegroundColor Yellow
$env:JWT__SIGNING_KEY = 'DEV_KEY_NOT_FOR_PROD_change_me_minimum_32_bytes_xx'
$env:DataDir = $dataDir
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = $Url
Write-Host "      JWT__SIGNING_KEY=<dev key>" -ForegroundColor Green
Write-Host "      DataDir=$dataDir" -ForegroundColor Green
Write-Host "      ASPNETCORE_URLS=$Url" -ForegroundColor Green

# 5 ─ Start server foreground ──────────────────────────────────────────
Write-Host "[5/5] Start server foreground (Ctrl+C để dừng)..." -ForegroundColor Yellow
Write-Host ""
Write-Host "==================================================================" -ForegroundColor Cyan
Write-Host "  SERVER ĐANG CHẠY: $Url" -ForegroundColor Cyan
Write-Host "  Client Revit dùng:" -ForegroundColor Cyan
Write-Host "    Email:    $Email" -ForegroundColor Cyan
Write-Host "    Password: $Password" -ForegroundColor Cyan
Write-Host "  Health check: curl $Url/health" -ForegroundColor Cyan
Write-Host "==================================================================" -ForegroundColor Cyan
Write-Host ""

Set-Location $serverDir
& dotnet run --no-build --no-launch-profile -c $Configuration --project $serverCsproj