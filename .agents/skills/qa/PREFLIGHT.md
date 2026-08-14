# Preflight Procedure

Run this before the GUI test pass.

## 1. Close SolidWorks

Check whether `SLDWORKS.exe` is running:

```powershell
Get-Process SLDWORKS -ErrorAction SilentlyContinue
```

Also check for any SolidWorks helper processes that can keep the add-in DLL open (e.g. `sldworks_fs`):

```powershell
Get-Process | Where-Object { $_.Name -like 'sldworks*' } | Select-Object Name, Id
```

If any are running, ask the user to close them and confirm before continuing. Building or registering while SolidWorks (or its helpers) is running can lock the add-in DLL. Unit tests (`dotnet test`) are an exception — they build into a separate `bin_unit_test` folder and can run while SolidWorks is open.

## 2. Build

```powershell
dotnet build "Solidworks Inventree Add-In.sln" --disable-build-servers
```

If the build fails, stop and ask the user to fix the branch before QA.

## 3. Run unit tests

```powershell
dotnet test "SwInventreeAddin.Tests/SwInventreeAddin.Tests.csproj" --disable-build-servers
```

If tests fail, stop and ask the user to fix the branch before QA.

## 4. Verify the built assembly version matches the git-derived version

For features that display a version or build identifier, confirm the add-in assembly is stamped with a version that reflects the current branch. For example, `v2.0.0-107-gce8b2dc` should produce assembly version `2.0.0.107`.

Get the git-derived version:

```powershell
$gitDesc = git describe --tags --always
$gitVersion = if ($gitDesc -match '^v?(\d+)\.(\d+)\.(\d+)(?:-(\d+))?') { $rev = if ($matches[4]) { $matches[4] } else { 0 }; "$($matches[1]).$($matches[2]).$($matches[3]).$rev" } else { 'unknown' }
$gitVersion
```

Get the built assembly version:

```powershell
[System.Reflection.AssemblyName]::GetAssemblyName("$(Get-Location)\SwInventreeAddin\bin\Debug\net48\SwInventreeAddin.dll").Version.ToString()
```

The two values must match. If they do not, the build pipeline may be defaulting to `1.0.0.0`. Stop and ask the user to fix the branch before QA.

## 5. Verify the registered add-in path

Before starting GUI testing, confirm that SolidWorks will load the dev build and not an installed release copy:

```powershell
reg query "HKLM\SOFTWARE\Classes\CLSID\{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}\InprocServer32" /s
```

Check the `CodeBase` value. It must point to the repo's `SwInventreeAddin\bin\Debug\net48\SwInventreeAddin.dll`. If it points to `C:\Program Files\SwInventreeAddin\SwInventreeAddin.DLL`, re-registration was skipped or failed.

Ignore the `Assembly` version in the registry — it is the version that was registered at install time and does not need to match the current build.

## 6. Register the dev build with SolidWorks (only if needed)

Per ADR-0007, registration is one-time per DLL path. Rebuilding in place is covered.

If the `CodeBase` value from step 5 already points to the repo's `SwInventreeAddin\bin\Debug\net48\SwInventreeAddin.dll`, re-registration is unnecessary — do not ask the user and do not re-register.

Re-register only when:
- The `CodeBase` path is wrong or the DLL path has changed,
- The add-in fails to load in SolidWorks,
- The user explicitly asks to re-register.

To re-register, run `DevRegister.ps1` as Administrator from the repo root:

```powershell
Start-Process PowerShell -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File '$(Get-Location)\DevRegister.ps1'" -Verb RunAs -Wait
```

Alternatively, the user may right-click `DevRegister.bat` and select **Run as administrator**.

If this shell is not running as Administrator, the elevation prompt may not appear and the registry will not be updated. In that case, ask the user to run `DevRegister.bat` as Administrator manually.

After registration, the user must restart SolidWorks.

## 7. Confirm InvenTree configuration

The InvenTree server URL and API key must already be configured in the Task Pane. Ask the user to confirm before starting GUI testing.
