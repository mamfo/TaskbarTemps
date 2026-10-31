# Verifica a persistencia das definicoes (gravacao atomica, .bak e recuperacao).
# Compila o Settings com um Main de teste e corre-o elevado, usando um ficheiro
# isolado em %TEMP% - nunca toca nas definicoes da aplicacao instalada.
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$libDir = Join-Path $root "lib\extract"
$out = Join-Path $PSScriptRoot "settings-test"

New-Item -ItemType Directory -Force -Path $out | Out-Null

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

$exe = Join-Path $out "settings-test.exe"
$sources = Get-ChildItem (Join-Path $root "src") -Filter *.cs |
    Where-Object { $_.Name -ne "Program.cs" } |
    ForEach-Object { $_.FullName }
$sources += (Join-Path $PSScriptRoot "settings-test.cs")

$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$compilerArgs = @(
    "/nologo", "/target:exe", "/optimize+", "/platform:anycpu",
    "/codepage:65001", ("/out:" + $exe),
    "/r:System.dll", "/r:System.Core.dll", "/r:System.Drawing.dll",
    "/r:System.Windows.Forms.dll", "/r:System.Web.Extensions.dll"
)
foreach ($dep in $deps) { $compilerArgs += ("/r:" + (Join-Path $libDir $dep)) }
$compilerArgs += $sources

& $csc $compilerArgs
if ($LASTEXITCODE -ne 0) { throw "A compilacao do verificador falhou." }

# Ficheiro de teste isolado, com escolhas distintas das predefinicoes.
$dataDir = Join-Path $env:TEMP "tt_settings_test"
New-Item -ItemType Directory -Force -Path $dataDir | Out-Null
$settings = Join-Path $dataDir "settings.ini"
@'
# TaskbarTemps - configuracao (teste)
IntervalMs=1000
Visible=True
OffsetX=0
OffsetY=0
Theme=auto
UsageWhenNoTemp=True
CheckUpdates=True
AutoInstall=True
ShowCpu=True
ShowRam=True
ShowGpu=True
ShowDisk=False
ShowNet=False
ShowIpLocal=False
ShowIpPublic=False
'@ | Set-Content $settings -Encoding UTF8

$report = Join-Path $dataDir "relatorio.txt"
if (Test-Path $report) { Remove-Item $report -Force }

$env:TBT_SETTINGS_FILE = $settings
$env:TBT_TEST_REPORT = $report

Write-Host "A pedir elevacao para o verificador correr (UAC)..."
$argList = '"' + $settings + '" "' + $report + '"'
$proc = Start-Process -FilePath $exe -Verb RunAs -Wait -PassThru -ArgumentList $argList
Write-Host "codigo de saida: $($proc.ExitCode)"
Write-Host "---- relatorio ----"
if (Test-Path $report) { Get-Content $report } else { Write-Host "SEM RELATORIO" }
