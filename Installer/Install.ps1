# OA InvenTree Add-In Installer
# Run this script as Administrator. It installs the add-in for all users
# on this machine and registers it with SolidWorks.

#region -- Admin check --
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
          ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))
{
    Write-Host ""
    Write-Host "ERROR: This script must be run as Administrator." -ForegroundColor Red
    Write-Host "Right-click Install.bat and choose 'Run as administrator'."
    Read-Host "Press Enter to exit"
    exit 1
}
#endregion

$installDir    = "C:\Program Files\OA InvenTree Addin"
$regasm        = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"
$addinDll      = Join-Path $installDir "SwInventreeAddin.dll"
$configDest    = Join-Path $installDir "inventree_servers.json"
$configSrc     = Join-Path $PSScriptRoot "inventree_servers.json"
$scriptDir     = $PSScriptRoot

Write-Host ""
Write-Host "OA InvenTree Add-In Installer" -ForegroundColor Cyan
Write-Host "================================" -ForegroundColor Cyan
Write-Host ""

# 1. Create install folder
Write-Host "Creating install folder: $installDir"
New-Item -ItemType Directory -Path $installDir -Force | Out-Null

# 2. Copy all DLLs and resource files
Write-Host "Copying add-in files..."
Get-ChildItem -Path $scriptDir -Include "*.dll","*.png","*.json" -File | ForEach-Object {
    # Don't overwrite an existing inventree_servers.json (preserve user's API key on upgrade)
    if ($_.Name -eq "inventree_servers.json" -and (Test-Path $configDest)) {
        Write-Host "  Keeping existing inventree_servers.json (preserving your API key)"
    } else {
        Copy-Item $_.FullName -Destination $installDir -Force
        Write-Host "  Copied: $($_.Name)"
    }
}

# Copy Resources subfolder
$resourcesSrc = Join-Path $scriptDir "Resources"
if (Test-Path $resourcesSrc) {
    $resourcesDest = Join-Path $installDir "Resources"
    New-Item -ItemType Directory -Path $resourcesDest -Force | Out-Null
    Copy-Item "$resourcesSrc\*" -Destination $resourcesDest -Force
    Write-Host "  Copied: Resources\"
}

# 3. Register with SolidWorks via RegAsm
Write-Host ""
Write-Host "Registering with SolidWorks..."
if (-not (Test-Path $regasm)) {
    Write-Host "ERROR: RegAsm not found at $regasm" -ForegroundColor Red
    Write-Host "Is .NET Framework 4.8 installed?"
    Read-Host "Press Enter to exit"
    exit 1
}

& $regasm $addinDll /codebase /s 2>&1 | Write-Host
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: RegAsm failed (exit code $LASTEXITCODE)" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

# 4. Open config file if it needs to be filled in
Write-Host ""
if (-not (Test-Path $configDest)) {
    Write-Host "ERROR: inventree_servers.json was not found in the installer folder." -ForegroundColor Red
    Write-Host "Copy inventree_servers.json next to Install.bat and run again."
    Read-Host "Press Enter to exit"
    exit 1
}

Write-Host "Installation complete!" -ForegroundColor Green
Write-Host ""
Write-Host "Next step: Edit inventree_servers.json to add your InvenTree URL and API key."
Write-Host "Opening the file now..."
Start-Process notepad $configDest
Write-Host ""
Write-Host "After saving the file, restart SolidWorks and the add-in will appear."
Read-Host "Press Enter to exit"
