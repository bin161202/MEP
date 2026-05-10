<#
.SYNOPSIS
    Lint cấm gọi ElementId.IntegerValue / ElementId.Value trực tiếp ngoài ElementIdAdapter.

.DESCRIPTION
    Quy tắc cứng (Rule 02 — multi-version + Rule 05 — anti-patterns):
    - File DUY NHẤT được phép gọi .IntegerValue / .Value trên ElementId là src/client/MEPAuto.Client.Common/Revit/ElementIdAdapter.cs
    - Mọi nơi khác phải đi qua ElementIdAdapter.GetValue(id) / Create(value) để compat 2022-2027
    - CI fail nếu phát hiện vi phạm. Local dev có thể chạy trước khi commit.

    Pattern check: regex `\.IntegerValue\b` (chính xác), `\.Value\b` chỉ flag khi đứng sau biến
    có tên chứa "id|Id" (giảm false positive vì .Value rất phổ biến).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools/verify-elementid-usage.ps1
#>

param(
    [string]$RepoRoot = (Resolve-Path "$PSScriptRoot/..").Path
)

$ErrorActionPreference = 'Stop'
$violations = @()

$adapterRelative = "src/client/MEPAuto.Client.Common/Revit/ElementIdAdapter.cs"
$adapterPath = Join-Path $RepoRoot $adapterRelative

# Files to scan: all .cs trong src/, shared/, tests/, trừ ElementIdAdapter
$files = Get-ChildItem -Path (Join-Path $RepoRoot "src"), (Join-Path $RepoRoot "shared"), (Join-Path $RepoRoot "tests") `
    -Filter *.cs -Recurse -ErrorAction SilentlyContinue |
    Where-Object {
        $_.FullName -notlike "*\bin\*" -and
        $_.FullName -notlike "*\obj\*" -and
        $_.FullName -ne $adapterPath
    }

foreach ($file in $files) {
    $lines = Get-Content $file.FullName
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        # Skip comment lines
        $trimmed = $line.TrimStart()
        if ($trimmed.StartsWith("//") -or $trimmed.StartsWith("*")) { continue }

        # Pattern 1: .IntegerValue (luôn cấm — chỉ ElementId mới có)
        if ($line -match '\.IntegerValue\b') {
            $violations += [PSCustomObject]@{
                File = $file.FullName.Substring($RepoRoot.Length + 1)
                Line = $i + 1
                Pattern = '.IntegerValue'
                Code = $line.Trim()
            }
        }

        # Pattern 2: <something with id|Id>.Value — chặt hơn để giảm false positive
        if ($line -match '\b\w*[Ii]d\w*\.Value\b' -and $line -notmatch 'ElementIdAdapter') {
            $violations += [PSCustomObject]@{
                File = $file.FullName.Substring($RepoRoot.Length + 1)
                Line = $i + 1
                Pattern = '<id-like>.Value'
                Code = $line.Trim()
            }
        }
    }
}

if ($violations.Count -eq 0) {
    Write-Host "OK: không phát hiện vi phạm ElementIdAdapter rule." -ForegroundColor Green
    exit 0
}

Write-Host "FAIL: $($violations.Count) vi phạm rule ElementIdAdapter (đi qua adapter thay vì gọi trực tiếp):" -ForegroundColor Red
$violations | ForEach-Object {
    Write-Host ("  {0}:{1} [{2}]  {3}" -f $_.File, $_.Line, $_.Pattern, $_.Code) -ForegroundColor Yellow
}
Write-Host ""
Write-Host "Fix: thay bằng ElementIdAdapter.GetValue(id) hoặc ElementIdAdapter.Create(value)." -ForegroundColor Cyan
Write-Host "Path adapter: $adapterRelative" -ForegroundColor Cyan
exit 1
