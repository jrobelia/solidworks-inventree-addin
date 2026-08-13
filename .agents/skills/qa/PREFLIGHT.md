# Preflight Procedure

Run this before the GUI test pass.

## 1. Close SolidWorks

Check whether `SLDWORKS.exe` is running:

```powershell
Get-Process SLDWORKS -ErrorAction SilentlyContinue
```

If it is running, ask the user to close SolidWorks and confirm before continuing. Building or registering while SolidWorks is running can lock the add-in DLL. Unit tests (`dotnet test`) are an exception — they build into a separate `bin_unit_test` folder and can run while SolidWorks is open.

## 2. Build

```powershell
dotnet build "Solidworks Inventree Add-In.sln"
```

If the build fails, stop and ask the user to fix the branch before QA.

## 3. Run unit tests

```powershell
dotnet test "SwInventreeAddin.Tests/SwInventreeAddin.Tests.csproj"
```

If tests fail, stop and ask the user to fix the branch before QA.

## 4. Register the dev build with SolidWorks

Per ADR-0007, registration is one-time per DLL path. Rebuilding in place is covered. Re-register only when the latest build fails to load or the DLL path changes.

Ask the user whether to re-register. If yes, run `DevRegister.ps1` as Administrator from the repo root:

```powershell
Start-Process PowerShell -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File '$(Get-Location)\DevRegister.ps1'" -Verb RunAs -Wait
```

Alternatively, the user may right-click `DevRegister.bat` and select **Run as administrator**.

After registration, the user must restart SolidWorks.

## 5. Confirm InvenTree configuration

The InvenTree server URL and API key must already be configured in the Task Pane. Ask the user to confirm before starting GUI testing.
