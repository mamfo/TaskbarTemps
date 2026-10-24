#Requires -Version 5.1
<#
    Cria o instalador autónomo TaskbarTempsSetup.exe.

    O executável inclui a build de dist (aplicação, DLLs e instalador do PawnIO)
    como recursos incorporados. Não é necessário .NET SDK.
#>
[CmdletBinding()]
param(
    [string]$Output = "",
    [switch]$SkipBuild,
    [switch]$Run
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dist = Join-Path $root "dist"
$version = [string](& (Join-Path $root "test\check-versions.ps1") -Root $root | Select-Object -Last 1)

if ([string]::IsNullOrWhiteSpace($Output)) {
    $Output = Join-Path $root "release"
} elseif (-not [System.IO.Path]::IsPathRooted($Output)) {
    $Output = Join-Path (Get-Location) $Output
}

if (-not $SkipBuild) {
    & (Join-Path $root "build.ps1") -Output $dist
    if ($LASTEXITCODE -ne 0) { throw "A compilação da aplicação falhou." }
}

$required = @("TaskbarTemps.exe", "TaskbarTemps.exe.config", "PawnIO_setup.exe")
foreach ($name in $required) {
    if (-not (Test-Path (Join-Path $dist $name))) {
        throw "Ficheiro em falta em dist: $name"
    }
}

Write-Host "TaskbarTemps - instalador" -ForegroundColor Cyan

$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { throw "Compilador .NET Framework não encontrado: $csc" }

$icon = Join-Path $root "assets\app.ico"
$manifest = Join-Path $root "app.manifest"
if (-not (Test-Path $icon)) { throw "Ícone em falta: $icon" }

# Copia a carga para uma pasta temporária sem espaços no caminho,
# evitando problemas de quoting no /resource do csc.
$stage = Join-Path $env:TEMP ("ttsetup_" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $stage | Out-Null

try {
    $payload = Get-ChildItem $dist -File | Where-Object { $_.Name -ne "README.md" }
    foreach ($file in $payload) {
        Copy-Item $file.FullName $stage -Force
    }

    $resources = @()
    foreach ($file in (Get-ChildItem $stage -File)) {
        $resources += ('/resource:{0},{1}' -f $file.FullName, $file.Name)
    }

    New-Item -ItemType Directory -Force -Path $Output | Out-Null
    $exe = Join-Path $Output "TaskbarTempsSetup.exe"

    $compilerArgs = @(
        "/nologo",
        "/target:winexe",
        "/optimize+",
        "/platform:anycpu",
        "/codepage:65001",
        "/out:$exe",
        "/win32icon:$icon",
        "/win32manifest:$manifest",
        "/r:System.dll",
        "/r:System.Core.dll",
        "/r:System.Drawing.dll",
        "/r:System.Windows.Forms.dll",
        (Join-Path $root "setup\Setup.cs"),
        (Join-Path $root "setup\SetupInfo.cs")
    )
    $compilerArgs += $resources

    & $csc $compilerArgs
    if ($LASTEXITCODE -ne 0) { throw "A compilação do instalador falhou (código $LASTEXITCODE)." }

    if (Test-Path (Join-Path $root "README.md")) {
        Copy-Item (Join-Path $root "README.md") (Join-Path $Output "README.md") -Force
    }

    $hash = (Get-FileHash -Path $exe -Algorithm SHA256).Hash.ToLowerInvariant()
    $hashFile = Join-Path $Output "TaskbarTempsSetup.exe.sha256"
    Set-Content -Path $hashFile -Value ($hash + "  TaskbarTempsSetup.exe") -Encoding ASCII

    $size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
    Write-Host ""
    Write-Host "OK: $exe ($size MB)" -ForegroundColor Green
    Write-Host "    versão: $version"
    Write-Host "    SHA-256: $hashFile" -ForegroundColor Green
    Write-Host "    ficheiros incluídos: $($payload.Count)"
} finally {
    Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
}

if ($Run) {
    Start-Process -FilePath (Join-Path $Output "TaskbarTempsSetup.exe")
}
