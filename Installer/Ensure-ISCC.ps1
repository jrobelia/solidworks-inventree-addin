# Ensure-ISCC.ps1  Prints the path to ISCC.exe, winget-installing Inno Setup
# when it is not already resolvable. Exits non-zero when Inno cannot be
# provided. The CI and release workflows both call this so the winget pin
# and the search order cannot drift apart. Local builds use
# Resolve-ISCC.ps1 directly — a missing compiler warns there rather than
# installing software on a dev machine.

$iscc = & "$PSScriptRoot\Resolve-ISCC.ps1"
if (-not $iscc) {
    winget install --id JRSoftware.InnoSetup -e --version 6.7.3 --accept-source-agreements --accept-package-agreements
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    $iscc = & "$PSScriptRoot\Resolve-ISCC.ps1"
}
if (-not $iscc) {
    Write-Host "ISCC.exe not found after winget install." -ForegroundColor Red
    exit 1
}
Write-Output $iscc
