# Package.ps1  Run this (no admin needed) to build and zip the installer.
# Output: Installer\OA-SWInvenTree-Addin.zip

$repoRoot   = Split-Path $PSScriptRoot -Parent
$buildOut   = "$repoRoot\SwInventreeAddin\bin\Release\net48"
$distDir    = "$repoRoot\Installer\dist"
$zipPath    = "$repoRoot\Installer\OA-SWInvenTree-Addin.zip"

Write-Host "Building add-in..." -ForegroundColor Cyan
Push-Location $repoRoot
dotnet build SwInventreeAddin/SwInventreeAddin.csproj -c Release --nologo -v quiet
if ($LASTEXITCODE -ne 0) { Write-Host "Build failed." -ForegroundColor Red; Pop-Location; exit 1 }
Pop-Location

Write-Host "Assembling distribution..." -ForegroundColor Cyan
Remove-Item $distDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $distDir | Out-Null

# Copy all DLLs from the build output
Get-ChildItem "$buildOut\*.dll" | Copy-Item -Destination $distDir

# Copy Resources subfolder
if (Test-Path "$buildOut\Resources") {
    New-Item -ItemType Directory -Path "$distDir\Resources" | Out-Null
    Copy-Item "$buildOut\Resources\*" -Destination "$distDir\Resources"
}

# Copy installer scripts
Copy-Item "$PSScriptRoot\Install.ps1"                         -Destination $distDir
Copy-Item "$PSScriptRoot\Install (Run as Administrator).bat"  -Destination $distDir
Copy-Item "$PSScriptRoot\Uninstall.ps1"                       -Destination $distDir
Copy-Item "$PSScriptRoot\Uninstall (Run as Administrator).bat" -Destination $distDir
Copy-Item "$PSScriptRoot\inventree_servers.json"              -Destination $distDir
Copy-Item "$PSScriptRoot\README.txt"                          -Destination $distDir

# Zip it
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path "$distDir\*" -DestinationPath $zipPath
Remove-Item $distDir -Recurse -Force

Write-Host ""
Write-Host "Done! Share this file with your coworker:" -ForegroundColor Green
Write-Host "  $zipPath"
