#Requires -Version 5.1
<#
    Confirma que a versão da aplicação, do instalador e do assembly coincidem.
    Devolve a versão (Major.Minor.Build) no fluxo de saída.
#>
[CmdletBinding()]
param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = "Stop"

function Read-Version {
    param(
        [string]$Path,
        [string]$Pattern,
        [string]$Label
    )

    if (-not (Test-Path $Path)) { throw "Ficheiro em falta: $Path" }
    $text = Get-Content $Path -Raw
    $match = [regex]::Match($text, $Pattern)
    if (-not $match.Success) { throw "$Label não encontrada em $Path." }
    return $match.Groups[1].Value
}

$setupDir = Join-Path $Root "setup"
$srcDir = Join-Path $Root "src"

$appVersion = Read-Version (Join-Path $setupDir "Setup.cs") `
    'AppVersion\s*=\s*"([0-9]+\.[0-9]+\.[0-9]+)"' "AppVersion"

$setupAssembly = Read-Version (Join-Path $setupDir "SetupInfo.cs") `
    'AssemblyVersion\("([0-9]+(?:\.[0-9]+){1,3})"\)' "AssemblyVersion"

$appAssembly = Read-Version (Join-Path $srcDir "AssemblyInfo.cs") `
    'AssemblyVersion\("([0-9]+(?:\.[0-9]+){1,3})"\)' "AssemblyVersion"

$setupFile = Read-Version (Join-Path $setupDir "SetupInfo.cs") `
    'AssemblyFileVersion\("([0-9]+(?:\.[0-9]+){1,3})"\)' "AssemblyFileVersion"

$appFile = Read-Version (Join-Path $srcDir "AssemblyInfo.cs") `
    'AssemblyFileVersion\("([0-9]+(?:\.[0-9]+){1,3})"\)' "AssemblyFileVersion"

foreach ($pair in @(
    @{ Name = "setup/SetupInfo.cs"; Value = $setupAssembly },
    @{ Name = "src/AssemblyInfo.cs"; Value = $appAssembly }
)) {
    if ($pair.Value -ne $appVersion -and -not $pair.Value.StartsWith($appVersion + ".")) {
        throw "$($pair.Name): AssemblyVersion $($pair.Value) não corresponde a AppVersion $appVersion."
    }
}

if ($setupAssembly -ne $appAssembly) {
    throw "AssemblyVersion difere entre setup/SetupInfo.cs ($setupAssembly) e src/AssemblyInfo.cs ($appAssembly)."
}

if ($setupFile -ne $setupAssembly) {
    throw "AssemblyFileVersion ($setupFile) difere de AssemblyVersion ($setupAssembly) em setup/SetupInfo.cs."
}

if ($appFile -ne $appAssembly) {
    throw "AssemblyFileVersion ($appFile) difere de AssemblyVersion ($appAssembly) em src/AssemblyInfo.cs."
}

Write-Host "Versões consistentes: aplicação $appVersion, assembly $appAssembly." -ForegroundColor Green
Write-Output $appVersion
