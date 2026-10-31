# Lado elevado do teste de atualização automática:
# pára a instalação atual e arranca a cópia antiga (test\update\TaskbarTemps.exe).
$log = Join-Path $env:TEMP "tt-elevated.log"

function Note([string]$text) {
    try { Add-Content -Path $log -Value ((Get-Date).ToString("HH:mm:ss") + "  " + $text) } catch { }
}

$ErrorActionPreference = "Continue"
$root = Split-Path -Parent $PSScriptRoot

try { if (Test-Path $log) { Remove-Item $log -Force } } catch { }
Note "helper iniciado (utilizador: $env:USERNAME)"

$procs = @(Get-Process TaskbarTemps -ErrorAction SilentlyContinue)
Note "processos encontrados: $($procs.Count)"
foreach ($p in $procs) {
    try {
        Note "a parar pid $($p.Id)..."
        Stop-Process -Id $p.Id -Force -ErrorAction Stop
        Note "parado"
    } catch {
        Note "falha ao parar $($p.Id): $($_.Exception.Message)"
    }
}
Start-Sleep -Seconds 2

$exe = Join-Path $PSScriptRoot "update\TaskbarTemps.exe"
if (-not (Test-Path $exe)) { Note "EXE EM FALTA: $exe"; exit 1 }

try {
    Start-Process -FilePath $exe -WorkingDirectory (Split-Path -Parent $exe)
    Note "cópia de teste arrancada"
} catch {
    Note "falha ao arrancar: $($_.Exception.Message)"
}

Note "helper terminou"
