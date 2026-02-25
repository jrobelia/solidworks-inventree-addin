@echo off
:: Launch Uninstall.ps1 as Administrator
PowerShell -NoProfile -ExecutionPolicy Bypass -Command "& { Start-Process PowerShell -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File ""%~dp0Uninstall.ps1""' -Verb RunAs -Wait }"
