$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$choco = (Get-Command choco -ErrorAction SilentlyContinue).Source
if ([string]::IsNullOrWhiteSpace($choco)) {
    Set-ExecutionPolicy Bypass -Scope Process -Force
    [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.ServicePointManager]::SecurityProtocol -bor 3072
    iex ((New-Object System.Net.WebClient).DownloadString('https://community.chocolatey.org/install.ps1'))
    $choco = 'C:\ProgramData\chocolatey\bin\choco.exe'
}

& $choco install dotnet-sdk netfx-4.8-devpack nuget.commandline -y

New-Item -ItemType Directory -Force -Path 'C:\sw-redist','C:\sw-interops' | Out-Null

$nuget = 'C:\ProgramData\chocolatey\bin\nuget.exe'
& $nuget install SolidWorks.Interop.sldworks -Version 32.1.0 -OutputDirectory 'C:\sw-interops' -Source 'https://api.nuget.org/v3/index.json'
& $nuget install SolidWorks.Interop.swconst -Version 32.1.0 -OutputDirectory 'C:\sw-interops' -Source 'https://api.nuget.org/v3/index.json'
& $nuget install SolidWorks.Interop.swpublished -Version 32.1.0 -OutputDirectory 'C:\sw-interops' -Source 'https://api.nuget.org/v3/index.json'

Get-ChildItem 'C:\sw-interops' -Recurse -Filter 'SolidWorks.Interop.*.dll' |
    Where-Object { $_.FullName -like '*\lib\*' } |
    Copy-Item -Destination 'C:\sw-redist' -Force

Set-Content -Path 'Directory.Build.props.user' -Value "`r`n<Project>`r`n  <PropertyGroup>`r`n    <SolidWorksApiRedist>C:\sw-redist</SolidWorksApiRedist>`r`n  </PropertyGroup>`r`n</Project>`r`n" -Encoding UTF8
