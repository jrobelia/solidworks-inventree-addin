# SwInventreeAddin Uninstaller

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
          ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))
{
    Write-Host "ERROR: Run as Administrator." -ForegroundColor Red
    Read-Host "Press Enter to exit"; exit 1
}

$installDir = "C:\Program Files\SwInventreeAddin"
$regasm     = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"
$addinDll   = Join-Path $installDir "SwInventreeAddin.dll"

Write-Host "SwInventreeAddin Uninstaller" -ForegroundColor Cyan
Write-Host ""

if (Test-Path $addinDll) {
    Write-Host "Unregistering from SolidWorks..."
    & $regasm $addinDll /u /s 2>&1 | Write-Host
}

Write-Host "Deleting add-in files..."
Remove-Item $installDir -Recurse -Force -ErrorAction SilentlyContinue

# Remove the Add/Remove Programs entry
$regPath = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\OAInvenTreeAddin"
if (Test-Path $regPath) {
    Remove-Item $regPath -Force
    Write-Host "Removed from Add/Remove Programs."
}

Write-Host "Done. Restart SolidWorks to complete removal." -ForegroundColor Green
Read-Host "Press Enter to exit"
