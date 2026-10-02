# Package.ps1  Run this (no admin needed) to build and zip the installer.
# Output: Installer\SwInventreeAddin-<version>.zip
# Version is derived from the nearest git tag (e.g. v1.0.0, or v1.0.0-3-gabcd123 if not on a tag).

$repoRoot   = Split-Path $PSScriptRoot -Parent
$buildOut   = "$repoRoot\SwInventreeAddin\bin\Release\net48"
$distDir    = "$repoRoot\Installer\dist"

# Derive version from git -- falls back to commit hash if no tags exist
$version    = & git -C $repoRoot describe --tags --always 2>$null
if (-not $version) { $version = "unknown" }

$zipPath    = "$repoRoot\Installer\SwInventreeAddin-$version.zip"

Write-Host "Building add-in..." -ForegroundColor Cyan
Push-Location $repoRoot
dotnet build SwInventreeAddin/SwInventreeAddin.csproj -c Release --nologo -v quiet --disable-build-servers
if ($LASTEXITCODE -ne 0) { Write-Host "Build failed." -ForegroundColor Red; Pop-Location; exit 1 }
Pop-Location

Write-Host "Assembling distribution..." -ForegroundColor Cyan
Remove-Item $distDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $distDir | Out-Null

# _addin\ holds everything except the top-level launcher and README.
# Users only need to see two items when they open the zip.
$addinDir = "$distDir\_addin"
New-Item -ItemType Directory -Path $addinDir | Out-Null

# Write version stamp so the installed copy is identifiable
Set-Content -Path "$addinDir\version.txt" -Value $version -Encoding UTF8

# Copy all DLLs from the build output
Get-ChildItem "$buildOut\*.dll" | Copy-Item -Destination $addinDir

# Copy Resources subfolder
if (Test-Path "$buildOut\Resources") {
    New-Item -ItemType Directory -Path "$addinDir\Resources" | Out-Null
    Copy-Item "$buildOut\Resources\*" -Destination "$addinDir\Resources"
}

# PS1 scripts + Uninstall launcher go into _addin\ (installer copies them to Program Files)
Copy-Item "$PSScriptRoot\Install.ps1"                          -Destination $addinDir
Copy-Item "$PSScriptRoot\Uninstall.ps1"                        -Destination $addinDir
Copy-Item "$PSScriptRoot\Uninstall (Run as Administrator).bat" -Destination $addinDir

# Only the Install launcher, README, and LICENSE sit at the zip root
Copy-Item "$PSScriptRoot\Install (Run as Administrator).bat"   -Destination $distDir
Copy-Item "$PSScriptRoot\README.txt"                           -Destination $distDir
Copy-Item "$repoRoot\LICENSE"                                  -Destination $distDir

# Zip it
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path "$distDir\*" -DestinationPath $zipPath
Remove-Item $distDir -Recurse -Force

# Build the Inno Setup exe when the compiler is available. CI and the release
# workflow assert the exe exists, so a missing ISCC fails there; locally a
# warning keeps the zip path usable without Inno installed.
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

# SHA256 sidecars — the unsigned binaries' only integrity check for users
foreach ($artifact in @($zipPath, $exePath)) {
    if (Test-Path $artifact) {
        $hash = (Get-FileHash $artifact -Algorithm SHA256).Hash.ToLower()
        Set-Content -Path "$artifact.sha256" -Value "$hash  $(Split-Path $artifact -Leaf)" -Encoding ASCII
    }
}

Write-Host ""
Write-Host "Done! Version $version — share this file with your coworker:" -ForegroundColor Green
Write-Host "  $zipPath"
if (Test-Path $exePath) { Write-Host "  $exePath" }
