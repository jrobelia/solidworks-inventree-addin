# DevRegister.ps1
# Re-points SolidWorks to the debug build in this repo.
# Run this (as Administrator) after installing the release build on your dev machine,
# or any time SolidWorks stops picking up your latest changes.

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
          ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))
{
    Write-Host "ERROR: Run DevRegister.bat as Administrator." -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

$regasm  = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"
$devDll  = Join-Path $PSScriptRoot "SwInventreeAddin\bin\Debug\net48\SwInventreeAddin.dll"

if (-not (Test-Path $devDll))
{
    Write-Host "ERROR: Dev build not found at:" -ForegroundColor Red
    Write-Host "  $devDll"
    Write-Host "Run 'dotnet build' first, then re-run this script."
    Read-Host "Press Enter to exit"
    exit 1
}

Write-Host "Registering dev build with SolidWorks..." -ForegroundColor Cyan
Write-Host "  $devDll"
& $regasm $devDll /codebase /s 2>&1 | Write-Host

if ($LASTEXITCODE -ne 0)
{
    Write-Host "ERROR: RegAsm failed (exit code $LASTEXITCODE)" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

Write-Host ""
Write-Host "Done! Restart SolidWorks to load the dev build." -ForegroundColor Green
Read-Host "Press Enter to exit"
