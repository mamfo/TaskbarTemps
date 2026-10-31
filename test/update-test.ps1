# Compila uma cópia da app com uma versão antiga (test\update\TaskbarTemps.exe)
# para validar o caminho de atualização automática contra a release publicada.
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $PSScriptRoot "update"
$libDir = Join-Path $root "lib\extract"
$version = "1.5.9.0"

New-Item -ItemType Directory -Force -Path $out | Out-Null

$temp = Join-Path $env:TEMP ("ttupdate_" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $temp | Out-Null

try {
    # versão antiga, para a app pensar que falta uma release
    $info = Get-Content (Join-Path $root "src\AssemblyInfo.cs") -Raw
    $info = [regex]::Replace($info, 'AssemblyVersion\("[0-9.]+"\)', "AssemblyVersion(`"$version`")")
    $info = [regex]::Replace($info, 'AssemblyFileVersion\("[0-9.]+"\)', "AssemblyFileVersion(`"$version`")")
    Set-Content -Path (Join-Path $temp "AssemblyInfo.cs") -Value $info -Encoding UTF8

    $deps = @(
        "LibreHardwareMonitorLib.dll", "HidSharp.dll", "RAMSPDToolkit-NDD.dll",
        "DiskInfoToolkit.dll", "BlackSharp.Core.dll", "System.Memory.dll",
        "System.Runtime.CompilerServices.Unsafe.dll", "System.Buffers.dll",
        "System.Numerics.Vectors.dll"
    )

    $sources = Get-ChildItem (Join-Path $root "src") -Filter *.cs |
        Where-Object { $_.Name -ne "AssemblyInfo.cs" } |
        ForEach-Object { $_.FullName }
    $sources += (Join-Path $temp "AssemblyInfo.cs")

    $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
    $compilerArgs = @(
        "/nologo", "/target:winexe", "/optimize+", "/platform:anycpu",
        "/codepage:65001",
        ("/out:" + (Join-Path $out "TaskbarTemps.exe")),
        "/r:System.dll", "/r:System.Core.dll", "/r:System.Drawing.dll",
        "/r:System.Windows.Forms.dll", "/r:System.Web.Extensions.dll"
    )
    foreach ($dep in $deps) { $compilerArgs += ("/r:" + (Join-Path $libDir $dep)) }
    $compilerArgs += $sources

    & $csc $compilerArgs
    if ($LASTEXITCODE -ne 0) { throw "A compilação falhou." }

    foreach ($dep in $deps) { Copy-Item (Join-Path $libDir $dep) $out -Force }
    Copy-Item (Join-Path $root "app.config") (Join-Path $out "TaskbarTemps.exe.config") -Force

    $v = (Get-Item (Join-Path $out "TaskbarTemps.exe")).VersionInfo.FileVersion
    Write-Host "cópia de teste compilada: $v"
} finally {
    Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
}
