<#
.SYNOPSIS
    Sinh feature project mới (Client + Server) từ template Hello-World pattern.

.DESCRIPTION
    Tạo cặp project MEPAuto.{Name} (Client) + MEPAuto.Server.{Name} (Server) +
    {Name}Dtos.cs trong shared/MEPAuto.Contracts/DTOs/. Sinh kèm {Name}Contract.cs (IFeatureContract
    impl — entry HEADLESS cho AI/CAD-PDF mode). Tự thêm vào MEPAuto.sln, Client.Shell + Server.Api
    references, register {Name}Service trong Program.cs DI.

    Sau khi script chạy xong:
      1. Edit logic Domain/Application/Command theo nhu cầu feature
      2. Build sln verify pass
      3. Reload qua RevitAddinManager
      4. Test theo MEMBER-DEV-WORKFLOW.md

.PARAMETER Name
    Tên feature (PascalCase, vd: DuctRouting, RenameElements). KHÔNG dấu space, KHÔNG kí tự đặc biệt.

.PARAMETER DisplayName
    Tên hiển thị trên ribbon button. Default: $Name có space giữa từ.

.PARAMETER Panel
    Tên panel ribbon. Default: "MEPAuto - General".

.PARAMETER Order
    Thứ tự button trong panel (1-99). Default: 50.

.PARAMETER WithUi
    Sinh thêm Views/{Name}Window.xaml + ViewModels/{Name}WindowViewModel.cs cho feature
    có WPF dialog input. Command sẽ dùng template UI thay cho template TaskDialog mặc định.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools/add-feature/new-feature.ps1 -Name DuctRouting -Panel "MEPAuto - HVAC" -Order 20

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools/add-feature/new-feature.ps1 -Name PipeSlope -Panel "MEPAuto - Plumbing" -Order 30 -WithUi

.NOTES
    Sau khi tạo, member CẦN:
    - Cấp license `{name}.basic` cho test user trong /var/data/licenses.json trên VPS
    - Build sln, reload, test
#>

param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Z][a-zA-Z0-9]+$')]
    [string]$Name,

    [string]$DisplayName,
    [string]$Panel = "MEPAuto - General",

    [ValidateRange(1, 99)]
    [int]$Order = 50,

    [switch]$WithUi,

    [string]$RepoRoot = ""
)

$ErrorActionPreference = 'Stop'

# Resolve RepoRoot ở runtime ($PSScriptRoot có thể chưa set lúc param default eval)
if (-not $RepoRoot) {
    $scriptDir = if ($PSScriptRoot) { $PSScriptRoot } elseif ($PSCommandPath) { Split-Path -Parent $PSCommandPath } else { $PWD.Path }
    $RepoRoot = (Resolve-Path "$scriptDir/../..").Path
}

# Defaults
if (-not $DisplayName) {
    # PascalCase → "Duct Routing"
    $DisplayName = ($Name -creplace '([A-Z])', ' $1').Trim()
}
$NameLower = $Name.ToLower()

Write-Host "==> Creating feature: $Name" -ForegroundColor Cyan
Write-Host "    DisplayName: $DisplayName"
Write-Host "    Panel:       $Panel"
Write-Host "    Order:       $Order"
Write-Host "    WithUi:      $WithUi"
Write-Host "    RepoRoot:    $RepoRoot"
Write-Host ""

# ===== Paths =====
$templateRoot = Join-Path $RepoRoot 'tools/add-feature/template-feature'
$clientDest = Join-Path $RepoRoot "src/client/features/MEPAuto.$Name"
$serverDest = Join-Path $RepoRoot "src/server/features/MEPAuto.Server.$Name"
$contractsDest = Join-Path $RepoRoot "shared/MEPAuto.Contracts/DTOs/${Name}Dtos.cs"
$slnPath = Join-Path $RepoRoot 'MEPAuto.sln'
$clientShellCsproj = Join-Path $RepoRoot 'src/client/MEPAuto.Client.Shell/MEPAuto.Client.Shell.csproj'
$serverApiCsproj = Join-Path $RepoRoot 'src/server/MEPAuto.Server.Api/MEPAuto.Server.Api.csproj'
$programCs = Join-Path $RepoRoot 'src/server/MEPAuto.Server.Api/Program.cs'

# ===== Validate =====
foreach ($dest in @($clientDest, $serverDest, $contractsDest)) {
    if (Test-Path $dest) {
        throw "Feature đã tồn tại: $dest"
    }
}
if (-not (Test-Path $templateRoot)) {
    throw "Template folder không tìm thấy: $templateRoot"
}
if (-not (Test-Path $slnPath)) {
    throw "MEPAuto.sln không tìm thấy: $slnPath"
}

# ===== Replacement table =====
$replacements = @{
    '{{Feature}}'      = $Name
    '{{FeatureLower}}' = $NameLower
    '{{DisplayName}}'  = $DisplayName
    '{{Panel}}'        = $Panel
    '{{Order}}'        = "$Order"
}

function Copy-Template {
    param(
        [string]$SourceTemplate,
        [string]$DestPath
    )
    # Đọc UTF-8 explicit — Get-Content -Raw ở PS 5.1 default ANSI codepage → mojibake với Vietnamese
    $content = [System.IO.File]::ReadAllText($SourceTemplate, [System.Text.Encoding]::UTF8)
    foreach ($k in $replacements.Keys) {
        $content = $content.Replace($k, $replacements[$k])
    }
    $destDir = Split-Path $DestPath -Parent
    if (-not (Test-Path $destDir)) { New-Item -ItemType Directory -Force -Path $destDir | Out-Null }
    [System.IO.File]::WriteAllText($DestPath, $content, [System.Text.UTF8Encoding]::new($false))
    Write-Host "    + $($DestPath.Substring($RepoRoot.Length + 1))"
}

# ===== Copy templates =====
Write-Host "==> Copy templates..." -ForegroundColor Cyan

# Client
Copy-Template "$templateRoot/client/Client.csproj.template" "$clientDest/MEPAuto.$Name.csproj"
Copy-Template "$templateRoot/client/Manifest/Manifest.cs.template" "$clientDest/Manifest/${Name}Manifest.cs"

if ($WithUi) {
    # WPF dialog: Command mở Window → ShowDialog → POST server với state từ VM
    Copy-Template "$templateRoot/client/Commands/CommandWithUi.cs.template" "$clientDest/Commands/${Name}Command.cs"
    Copy-Template "$templateRoot/client/Views/Window.xaml.template" "$clientDest/Views/${Name}Window.xaml"
    Copy-Template "$templateRoot/client/Views/Window.xaml.cs.template" "$clientDest/Views/${Name}Window.xaml.cs"
    Copy-Template "$templateRoot/client/ViewModels/WindowViewModel.cs.template" "$clientDest/ViewModels/${Name}WindowViewModel.cs"
} else {
    Copy-Template "$templateRoot/client/Commands/Command.cs.template" "$clientDest/Commands/${Name}Command.cs"
}

# Contract HEADLESS — bắt buộc mọi feature (AI/CAD-PDF mode entry point)
Copy-Template "$templateRoot/client/Contracts/Contract.cs.template" "$clientDest/Contracts/${Name}Contract.cs"

# Server
Copy-Template "$templateRoot/server/Server.csproj.template" "$serverDest/MEPAuto.Server.$Name.csproj"
Copy-Template "$templateRoot/server/Domain/Logic.cs.template" "$serverDest/Domain/${Name}Logic.cs"
Copy-Template "$templateRoot/server/Application/Service.cs.template" "$serverDest/Application/$($Name)Service.cs"
Copy-Template "$templateRoot/server/Endpoint/Controller.cs.template" "$serverDest/Endpoint/${Name}Controller.cs"

# Contracts
Copy-Template "$templateRoot/contracts/Dtos.cs.template" $contractsDest

# ===== Add to sln =====
Write-Host ""
Write-Host "==> dotnet sln add..." -ForegroundColor Cyan
$clientCsproj = "$clientDest/MEPAuto.$Name.csproj"
$serverCsproj = "$serverDest/MEPAuto.Server.$Name.csproj"
& dotnet sln $slnPath add $clientCsproj 2>&1 | Where-Object { $_ -notmatch '^$' } | ForEach-Object { Write-Host "    $_" }
if ($LASTEXITCODE -ne 0) { throw "dotnet sln add failed cho client csproj" }
& dotnet sln $slnPath add $serverCsproj 2>&1 | Where-Object { $_ -notmatch '^$' } | ForEach-Object { Write-Host "    $_" }
if ($LASTEXITCODE -ne 0) { throw "dotnet sln add failed cho server csproj" }

# ===== Add ProjectReference =====
Write-Host ""
Write-Host "==> dotnet add reference..." -ForegroundColor Cyan
& dotnet add $clientShellCsproj reference $clientCsproj 2>&1 | Where-Object { $_ -notmatch '^$' } | ForEach-Object { Write-Host "    $_" }
if ($LASTEXITCODE -ne 0) { throw "dotnet add reference failed cho Client.Shell" }
& dotnet add $serverApiCsproj reference $serverCsproj 2>&1 | Where-Object { $_ -notmatch '^$' } | ForEach-Object { Write-Host "    $_" }
if ($LASTEXITCODE -ne 0) { throw "dotnet add reference failed cho Server.Api" }

# ===== Register service in Program.cs =====
Write-Host ""
Write-Host "==> Register service trong Program.cs..." -ForegroundColor Cyan
# Đọc UTF-8 explicit — Get-Content default ở PS 5.1 dùng system codepage → mojibake với Vietnamese
$lines = [System.IO.File]::ReadAllLines($programCs, [System.Text.Encoding]::UTF8)
$out = @()
$inserted = $false
$insertLine = "builder.Services.AddScoped<MEPAuto.Server.$Name.Application.$($Name)Service>();"
foreach ($line in $lines) {
    $out += $line
    if (-not $inserted -and $line -match '// ---- Feature services') {
        $out += $insertLine
        $inserted = $true
    }
}
if (-not $inserted) {
    Write-Host "    WARN: marker '// ---- Feature services' không tìm thấy trong Program.cs." -ForegroundColor Yellow
    Write-Host "    Hãy thêm dòng sau manually trước 'var app = builder.Build();' :" -ForegroundColor Yellow
    Write-Host "      $insertLine" -ForegroundColor Yellow
} else {
    [System.IO.File]::WriteAllLines($programCs, $out, [System.Text.UTF8Encoding]::new($false))
    Write-Host "    + Inserted: $insertLine"
}

# ===== Verify build =====
Write-Host ""
Write-Host "==> Verify build (dotnet build MEPAuto.sln -c Release-2024)..." -ForegroundColor Cyan
$buildOutput = & dotnet build $slnPath -c Release-2024 --nologo 2>&1
$buildExitCode = $LASTEXITCODE
$buildOutput | Select-Object -Last 8 | ForEach-Object { Write-Host "    $_" }
if ($buildExitCode -ne 0) {
    Write-Host ""
    Write-Host "BUILD FAILED — feature đã tạo nhưng build lỗi. Inspect files + fix." -ForegroundColor Red
    exit 1
}

# ===== Done =====
Write-Host ""
Write-Host "DONE. Feature '$Name' đã sẵn sàng." -ForegroundColor Green
Write-Host ""
Write-Host "Next steps cho member:" -ForegroundColor Cyan
Write-Host "  1. Edit Domain logic:    src/server/features/MEPAuto.Server.$Name/Domain/${Name}Logic.cs"
Write-Host "  2. Edit Application:     src/server/features/MEPAuto.Server.$Name/Application/$($Name)Service.cs"
Write-Host "  3. Edit Client command:  src/client/features/MEPAuto.$Name/Commands/${Name}Command.cs"
if ($WithUi) {
    Write-Host "     Edit Window XAML:    src/client/features/MEPAuto.$Name/Views/${Name}Window.xaml"
    Write-Host "     Edit ViewModel:      src/client/features/MEPAuto.$Name/ViewModels/${Name}WindowViewModel.cs"
}
Write-Host "  4. Edit Contracts DTO:   shared/MEPAuto.Contracts/DTOs/${Name}Dtos.cs"
Write-Host "  4b. Verify Contract:     src/client/features/MEPAuto.$Name/Contracts/${Name}Contract.cs"
Write-Host "       (sync InputType khi đổi signature ExecuteHeadless — xem WORKFLOW-NEW-FEATURE.md §6.2)"
Write-Host "  5. Tren VPS — cap license '$($NameLower).basic' cho user test:"
Write-Host "       ssh root@VPS_IP"
Write-Host "       Edit /var/data/licenses.json: them '$($NameLower).basic' vao mang license cua email user"
Write-Host "       docker compose -f /opt/MEPAuto/tools/deploy/docker-compose.yml restart api"
Write-Host "  6. Build sln (Ctrl+Shift+B) → Reload qua RevitAddinManager → click ribbon button → test."
Write-Host ""
Write-Host "Reference: docs/workflow/WORKFLOW-NEW-FEATURE.md cho checklist đầy đủ."
