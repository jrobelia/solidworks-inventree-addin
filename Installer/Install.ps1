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
Get-ChildItem -Path "$scriptDir\*" -Include "*.dll","*.png" -File | ForEach-Object {
    Copy-Item $_.FullName -Destination $installDir -Force
    Write-Host "  Copied: $($_.Name)"
}

# Copy Resources subfolder
$resourcesSrc = Join-Path $scriptDir "Resources"
if (Test-Path $resourcesSrc) {
    $resourcesDest = Join-Path $installDir "Resources"
    New-Item -ItemType Directory -Path $resourcesDest -Force | Out-Null
    Copy-Item "$resourcesSrc\*" -Destination $resourcesDest -Force
    Write-Host "  Copied: Resources\"
}

# Copy uninstaller into the install folder so the user can delete the download
Copy-Item (Join-Path $scriptDir "Uninstall.ps1")                        -Destination $installDir -Force
Copy-Item (Join-Path $scriptDir "Uninstall (Run as Administrator).bat") -Destination $installDir -Force
Write-Host "  Copied: Uninstaller"

# Unblock all copied files (Windows blocks DLLs extracted from a downloaded zip)
Get-ChildItem -Path $installDir -File -Recurse | Unblock-File

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

# 4. Register in Add/Remove Programs so Windows knows this is installed
#    and the user can uninstall from Settings without the original download.
Write-Host ""
Write-Host "Registering in Add/Remove Programs..."
$version     = "unknown"
$versionFile = Join-Path $scriptDir "version.txt"
if (Test-Path $versionFile) { $version = (Get-Content $versionFile -Raw).Trim() }

$uninstallBat = Join-Path $installDir "Uninstall (Run as Administrator).bat"
$regPath      = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\OAInvenTreeAddin"
New-Item -Path $regPath -Force | Out-Null
Set-ItemProperty -Path $regPath -Name "DisplayName"     -Value "OA InvenTree Add-In"
Set-ItemProperty -Path $regPath -Name "DisplayVersion"  -Value $version
Set-ItemProperty -Path $regPath -Name "Publisher"       -Value "OA"
Set-ItemProperty -Path $regPath -Name "InstallLocation" -Value $installDir
Set-ItemProperty -Path $regPath -Name "UninstallString" -Value $uninstallBat
Set-ItemProperty -Path $regPath -Name "NoModify"        -Value 1 -Type DWord
Set-ItemProperty -Path $regPath -Name "NoRepair"        -Value 1 -Type DWord
Write-Host "  Registered as version $version"

Write-Host "Installation complete!" -ForegroundColor Green
Write-Host ""
Write-Host "Start SolidWorks and configure your server via the Settings button in the InvenTree panel."
Read-Host "Press Enter to exit"
