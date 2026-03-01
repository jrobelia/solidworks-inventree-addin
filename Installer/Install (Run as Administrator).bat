@echo off
:: Launch Install.ps1 as Administrator
PowerShell -NoProfile -ExecutionPolicy Bypass -Command "& { Start-Process PowerShell -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File ""%~dp0_addin\Install.ps1""' -Verb RunAs -Wait }"
