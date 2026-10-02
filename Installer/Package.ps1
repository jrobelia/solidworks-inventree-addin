# Package.ps1  Run this (no admin needed) to build the installer exe.
# Output: Installer\SwInventreeAddin-<version>-Setup.exe
# Version is derived from the nearest git tag (e.g. v1.0.0, or v1.0.0-3-gabcd123 if not on a tag).

$repoRoot   = Split-Path $PSScriptRoot -Parent

# Derive version from git -- falls back to commit hash if no tags exist
$version    = & git -C $repoRoot describe --tags --always 2>$null
if (-not $version) { $version = "unknown" }

Write-Host "Building add-in..." -ForegroundColor Cyan
Push-Location $repoRoot
dotnet build SwInventreeAddin/SwInventreeAddin.csproj -c Release --nologo -v quiet --disable-build-servers
if ($LASTEXITCODE -ne 0) { Write-Host "Build failed." -ForegroundColor Red; Pop-Location; exit 1 }
Pop-Location

# Compile the Inno Setup exe when the compiler is available. CI and the
# release workflow assert the exe exists, so a missing ISCC fails there;
# locally a warning keeps the build usable without Inno installed.
$iscc = @(
    $env:INNO_SETUP,
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

$exePath = "$repoRoot\Installer\SwInventreeAddin-$($version.TrimStart('v'))-Setup.exe"
if ($iscc) {
    Write-Host "Building Setup.exe with Inno Setup..." -ForegroundColor Cyan
    & $iscc "/DAppVersion=$($version.TrimStart('v'))" "$PSScriptRoot\Setup.iss"
    if ($LASTEXITCODE -ne 0) { Write-Host "ISCC failed." -ForegroundColor Red; exit 1 }
} else {
    Write-Host "WARNING: ISCC.exe not found — Setup.exe skipped." -ForegroundColor Yellow
    Write-Host "  Install Inno Setup 6 or set `$env:INNO_SETUP to ISCC.exe's path."
}

Write-Host ""
Write-Host "Done! Version $version" -ForegroundColor Green
if (Test-Path $exePath) { Write-Host "  $exePath" }
