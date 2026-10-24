#Requires -Version 5.1
<#
    Compila o TaskbarTemps com o compilador .NET Framework incluído no Windows.
    Não é necessário instalar o .NET SDK.
#>
[CmdletBinding()]
param(
    [string]$Output = "",
    [switch]$NoAdminManifest,
    [switch]$Run,
    [switch]$Clean
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

if ([string]::IsNullOrWhiteSpace($Output)) {
    $Output = Join-Path $root "dist"
} elseif (-not [System.IO.Path]::IsPathRooted($Output)) {
    $Output = Join-Path (Get-Location) $Output
}

Write-Host "TaskbarTemps - compilação" -ForegroundColor Cyan
Write-Host "  origem: $root"
Write-Host "  destino: $Output"

$libDir = Join-Path $root "lib\extract"
$libDll = Join-Path $libDir "LibreHardwareMonitorLib.dll"

if (-not (Test-Path $libDll)) {
    Write-Host "A obter LibreHardwareMonitor 0.9.6..." -ForegroundColor Yellow
    $libRoot = Join-Path $root "lib"
    $zip = Join-Path $libRoot "LibreHardwareMonitor.zip"
    New-Item -ItemType Directory -Force -Path $libRoot | Out-Null
    if (-not (Test-Path $zip)) {
        Invoke-WebRequest -UseBasicParsing -Uri `
            "https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/releases/download/v0.9.6/LibreHardwareMonitor.zip" `
            -OutFile $zip
    }
    $tmp = Join-Path $env:TEMP ("lhm_" + [guid]::NewGuid().ToString("N"))
    Expand-Archive -Path $zip -DestinationPath $tmp -Force
    New-Item -ItemType Directory -Force -Path $libDir | Out-Null
    Copy-Item (Join-Path $tmp "*") $libDir -Recurse -Force
    Remove-Item $tmp -Recurse -Force
}

$pawnIoUrl = "https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe"
$pawnIoCache = Join-Path $root "lib\PawnIO_setup.exe"

function Get-PawnIoInstaller {
    param([string]$Destination)

    if (Test-Path $pawnIoCache) {
        Copy-Item $pawnIoCache $Destination -Force
        Write-Host "  PawnIO: instalador obtido da cache." -ForegroundColor DarkGray
        return $true
    }
    if (Test-Path $Destination) {
        Copy-Item $Destination $pawnIoCache -Force
        Write-Host "  PawnIO: instalador já presente no destino." -ForegroundColor DarkGray
        return $true
    }

    try {
        Write-Host "  A obter PawnIO 2.2.0..." -ForegroundColor Yellow
        Invoke-WebRequest -UseBasicParsing -Uri $pawnIoUrl -OutFile $Destination -TimeoutSec 60
        Copy-Item $Destination $pawnIoCache -Force
        return $true
    } catch {
        Write-Warning ("Não foi possível obter o instalador do PawnIO: " + $_.Exception.Message)
        Write-Warning ("Transfira manualmente $pawnIoUrl para " + (Split-Path -Parent $Destination))
        return $false
    }
}

$deps = @(
    "LibreHardwareMonitorLib.dll",
    "HidSharp.dll",
    "RAMSPDToolkit-NDD.dll",
    "DiskInfoToolkit.dll",
    "BlackSharp.Core.dll",
    "System.Memory.dll",
    "System.Runtime.CompilerServices.Unsafe.dll",
    "System.Buffers.dll",
    "System.Numerics.Vectors.dll"
)

foreach ($dep in $deps) {
    if (-not (Test-Path (Join-Path $libDir $dep))) {
        throw "Dependência em falta: $dep (execute o script novamente para descarregar)."
    }
}

$icon = Join-Path $root "assets\app.ico"
if (-not (Test-Path $icon)) {
    Write-Host "A gerar ícone..." -ForegroundColor Yellow
    & (Join-Path $root "tools\make-icon.ps1")
}

$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    throw "Compilador .NET Framework não encontrado: $csc"
}

if ($Clean -and (Test-Path $Output)) {
    Remove-Item $Output -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $Output | Out-Null

$exe = Join-Path $Output "TaskbarTemps.exe"
$refs = @(
    "System.dll",
    "System.Core.dll",
    "System.Drawing.dll",
    "System.Windows.Forms.dll",
    "System.Web.Extensions.dll"
)
foreach ($dep in $deps) {
    $refs += (Join-Path $libDir $dep)
}

$sources = Get-ChildItem (Join-Path $root "src") -Filter *.cs |
    Sort-Object Name |
    ForEach-Object { $_.FullName }

$compilerArgs = @(
    "/nologo",
    "/target:winexe",
    "/optimize+",
    "/platform:anycpu",
    "/codepage:65001",
    "/out:$exe",
    "/win32icon:$icon"
)

if (-not $NoAdminManifest) {
    $compilerArgs += "/win32manifest:" + (Join-Path $root "app.manifest")
}

foreach ($reference in $refs) {
    $compilerArgs += "/r:$reference"
}
$compilerArgs += $sources

& $csc $compilerArgs
if ($LASTEXITCODE -ne 0) {
    throw "A compilação falhou (código $LASTEXITCODE)."
}

foreach ($dep in $deps) {
    Copy-Item (Join-Path $libDir $dep) $Output -Force
}
Copy-Item (Join-Path $root "app.config") (Join-Path $Output "TaskbarTemps.exe.config") -Force
if (Test-Path (Join-Path $root "README.md")) {
    Copy-Item (Join-Path $root "README.md") $Output -Force
}

$pawnIoInOutput = Get-PawnIoInstaller -Destination (Join-Path $Output "PawnIO_setup.exe")

$mode = if ($NoAdminManifest) { "sem manifesto de administrador (testes)" } else { "com elevação (requireAdministrator)" }
Write-Host ""
Write-Host "OK: $exe" -ForegroundColor Green
Write-Host "    $mode"
if ($pawnIoInOutput) {
    Write-Host "    PawnIO 2.2.0: $(Join-Path $Output 'PawnIO_setup.exe')" -ForegroundColor Green
} else {
    Write-Host "    PawnIO 2.2.0: NÃO incluído (sem rede). Ver README.md." -ForegroundColor Yellow
}

if ($Run) {
    Start-Process -FilePath $exe
}
