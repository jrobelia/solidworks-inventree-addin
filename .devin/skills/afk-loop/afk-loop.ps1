$ErrorActionPreference = 'Stop'
$candidates = @(
    "$env:ProgramFiles\Git\bin\bash.exe",
    "${env:ProgramFiles(x86)}\Git\bin\bash.exe",
    "$env:LOCALAPPDATA\Programs\Git\bin\bash.exe"
)
$bash = $candidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $bash) {
    $command = Get-Command bash -ErrorAction SilentlyContinue
    if ($command -and $command.Source -notlike '*System32*') { $bash = $command.Source }
}
if (-not $bash) { throw 'Git for Windows Bash is required; WSL Bash is not supported.' }
if (-not (Get-Command python -ErrorAction SilentlyContinue)) { throw 'Python 3.9 or newer must be on PATH.' }
if (-not $env:DEVIN) {
    $bundled = Join-Path $env:LOCALAPPDATA 'Programs\Devin\resources\app\extensions\windsurf\devin\bin\devin.exe'
    if (Test-Path $bundled) { $env:DEVIN = $bundled }
}
$root = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
Push-Location $root
try {
    & $bash ($PSScriptRoot.Replace('\', '/') + '/afk-loop.sh') @args
    exit $LASTEXITCODE
} finally {
    Pop-Location
}
