<#
.SYNOPSIS
    Resolve version cho MEPAuto build (từ git tag hoặc fallback dev).

.DESCRIPTION
    Logic:
      1. Nếu HEAD đúng tag vX.Y.Z → version = X.Y.Z
      2. Nếu HEAD sau tag → version = X.Y.Z-dev.<commits>+<sha>
      3. Nếu không có tag nào → version = 0.0.0-dev+<sha>
      4. Nếu không trong git repo → version = 0.0.0-local

    Output 3 dạng:
      - SemVer đầy đủ (informational): trả qua return value
      - MSI version (3 phần X.Y.Z)
      - Assembly version (4 phần X.Y.Z.0): cho [AssemblyVersion]

.PARAMETER AsJson
    Output JSON thay vì text — cho CI parse.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools/version-stamp.ps1
    # → 0.1.0-dev.5+a1b2c3d

    powershell -ExecutionPolicy Bypass -File tools/version-stamp.ps1 -AsJson
    # → { "semver": "0.1.0-dev.5+a1b2c3d", "msi": "0.1.0", "assembly": "0.1.0.0" }
#>

param(
    [switch]$AsJson
)

$ErrorActionPreference = 'Stop'

function Invoke-Git {
    param([string[]]$GitArgs)
    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & git @GitArgs 2>$null
        return @{ output = $output; exit = $LASTEXITCODE }
    }
    finally {
        $ErrorActionPreference = $prev
    }
}

function Get-VersionInfo {
    $gitAvailable = $null -ne (Get-Command git -ErrorAction SilentlyContinue)
    if (-not $gitAvailable) {
        return @{ semver = "0.0.0-local"; msi = "0.0.0"; assembly = "0.0.0.0" }
    }

    $r = Invoke-Git @('rev-parse', '--git-dir')
    if ($r.exit -ne 0) {
        return @{ semver = "0.0.0-local"; msi = "0.0.0"; assembly = "0.0.0.0" }
    }

    $sha = (Invoke-Git @('rev-parse', '--short', 'HEAD')).output.Trim()

    $r2 = Invoke-Git @('describe', '--tags', '--match', 'v*')
    $describe = $r2.output
    $hasTag = $r2.exit -eq 0

    if (-not $hasTag) {
        $semver = "0.0.0-dev+$sha"
        $msi = "0.0.0"
    }
    elseif ($describe -match '^v(\d+\.\d+\.\d+)(-[\w\.]+)?-(\d+)-g([0-9a-f]+)$') {
        $base = $Matches[1]
        $suffix = $Matches[2]
        $commitsAhead = $Matches[3]
        $semver = "$base$suffix-dev.$commitsAhead+$sha"
        $msi = $base
    }
    elseif ($describe -match '^v(\d+\.\d+\.\d+)(-[\w\.]+)?$') {
        $base = $Matches[1]
        $suffix = $Matches[2]
        $semver = "$base$suffix"
        $msi = $base
    }
    else {
        Write-Warning "git describe output không match pattern: '$describe'. Fallback dev version."
        $semver = "0.0.0-dev+$sha"
        $msi = "0.0.0"
    }

    return @{
        semver   = $semver
        msi      = $msi
        assembly = "$msi.0"
    }
}

$info = Get-VersionInfo

if ($AsJson) {
    $info | ConvertTo-Json -Compress
} else {
    $info.semver
}
