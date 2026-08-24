param(
    [string]$AssemblyPath = "$PSScriptRoot\bin\Debug\net48\SwInventreeAddin.dll"
)

$gitVersion = & "$PSScriptRoot\SetGitVersion.ps1"
$asmVersion = [System.Reflection.AssemblyName]::GetAssemblyName((Resolve-Path $AssemblyPath)).Version.ToString()

if ($gitVersion -ne $asmVersion) {
    throw "MISMATCH: git $gitVersion vs assembly $asmVersion"
}

"OK: $gitVersion"
