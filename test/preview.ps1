# Compila uma pré-visualização sem elevação e com uma instância própria
# (mutex diferente), para poder ser testada enquanto a versão instalada corre.
# Uso: powershell -File test\preview.ps1 [-ShowIp]
param(
    [switch]$ShowIp,
    [switch]$KeepRunning
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $PSScriptRoot "preview"
$libDir = Join-Path $root "lib\extract"

New-Item -ItemType Directory -Force -Path $out | Out-Null

$temp = Join-Path $env:TEMP ("ttpreview_" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $temp | Out-Null

try {
    $program = Get-Content (Join-Path $root "src\Program.cs") -Raw
    $program = $program.Replace(
        'Local\\TaskbarTemps.SingleInstance',
        'Local\\TaskbarTemps.SingleInstance.Preview')
    Set-Content -Path (Join-Path $temp "Program.cs") -Value $program -Encoding UTF8

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

    $sources = Get-ChildItem (Join-Path $root "src") -Filter *.cs |
        Where-Object { $_.Name -ne "Program.cs" } |
        ForEach-Object { $_.FullName }
    $sources += (Join-Path $temp "Program.cs")

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
    if ($LASTEXITCODE -ne 0) { throw "A compilação da pré-visualização falhou." }

    foreach ($dep in $deps) { Copy-Item (Join-Path $libDir $dep) $out -Force }
    Copy-Item (Join-Path $root "app.config") (Join-Path $out "TaskbarTemps.exe.config") -Force
} finally {
    Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
}

Get-Process TaskbarTemps -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -like "$out*" } |
    Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

# As pré-visualizações usam um ficheiro de definições próprio, para nunca
# mexerem nas definições da aplicação instalada.
$settings = Join-Path $out "settings.ini"
if (-not (Test-Path $settings)) {
    @'
# TaskbarTemps - configuração (pré-visualização)
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
ShowDisk=True
ShowNet=True
ShowIpLocal=False
ShowIpPublic=False
'@ | Set-Content $settings -Encoding UTF8
}

if ($ShowIp) {
    $text = Get-Content $settings -Raw
    $value = $ShowIp.ToString()
    $text = [regex]::Replace($text, '(?m)^ShowIpLocal=.*$', "ShowIpLocal=$value")
    $text = [regex]::Replace($text, '(?m)^ShowIpPublic=.*$', "ShowIpPublic=$value")
    [System.IO.File]::WriteAllText($settings, $text, (New-Object System.Text.UTF8Encoding($false)))
}

$env:TBT_SETTINGS_FILE = $settings
Start-Process -FilePath (Join-Path $out "TaskbarTemps.exe")
Start-Sleep -Seconds 6

if (-not $KeepRunning) {
    Write-Host "Pré-visualização em execução: $(Join-Path $out 'TaskbarTemps.exe')"
}
