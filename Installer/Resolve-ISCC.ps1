# Resolve-ISCC.ps1  Prints the path to Inno Setup 6's ISCC.exe, or nothing
# when it is not installed. One home for the search order so Package.ps1
# (warn locally) and the CI workflows (winget-install) cannot drift apart.

@(
    $env:INNO_SETUP,
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
