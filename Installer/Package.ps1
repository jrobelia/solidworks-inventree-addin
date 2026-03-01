# Package.ps1  Run this (no admin needed) to build and zip the installer.
# Output: Installer\OA-SWInvenTree-Addin-<version>.zip
# Version is derived from the nearest git tag (e.g. v1.0.0, or v1.0.0-3-gabcd123 if not on a tag).

$repoRoot   = Split-Path $PSScriptRoot -Parent
$buildOut   = "$repoRoot\SwInventreeAddin\bin\Release\net48"
$distDir    = "$repoRoot\Installer\dist"

# Derive version from git — falls back to commit hash if no tags exist
$version    = & git -C $repoRoot describe --tags --always 2>$null
if (-not $version) { $version = "unknown" }

$zipPath    = "$repoRoot\Installer\OA-SWInvenTree-Addin-$version.zip"

Write-Host "Building add-in..." -ForegroundColor Cyan
Push-Location $repoRoot
dotnet build SwInventreeAddin/SwInventreeAddin.csproj -c Release --nologo -v quiet
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

# Only the Install launcher and README sit at the zip root
Copy-Item "$PSScriptRoot\Install (Run as Administrator).bat"   -Destination $distDir
Copy-Item "$PSScriptRoot\README.txt"                           -Destination $distDir

# Zip it
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path "$distDir\*" -DestinationPath $zipPath
Remove-Item $distDir -Recurse -Force

Write-Host ""
Write-Host "Done! Version $version — share this file with your coworker:" -ForegroundColor Green
Write-Host "  $zipPath"
