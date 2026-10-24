#Requires -Version 5.1
<#
    Publica uma versão do TaskbarTemps no GitHub.

    Compila a aplicação e o instalador, cria a etiqueta v<versão>, envia-a e
    cria a release com o TaskbarTempsSetup.exe anexado. A versão é lida de
    setup/Setup.cs (AppVersion), pelo que basta atualizá-la antes de publicar.

    Exemplos:
        .\publish-release.ps1 -DryRun       # mostra o que faria
        .\publish-release.ps1               # publica a versão atual
        .\publish-release.ps1 -NotesFile notas.md
#>
[CmdletBinding()]
param(
    [switch]$DryRun,
    [string]$NotesFile = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

function Get-Tool {
    param([string]$Name, [string]$Fallback)

    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    if ($Fallback -and (Test-Path $Fallback)) { return $Fallback }
    throw "Ferramenta não encontrada: $Name. Instale-a ou adicione-a ao PATH."
}

$git = Get-Tool "git" (Join-Path $env:LOCALAPPDATA "Programs\devtools\git\cmd\git.exe")
$gh = Get-Tool "gh" (Join-Path $env:LOCALAPPDATA "Programs\devtools\gh\gh.exe")
$env:GIT_TERMINAL_PROMPT = "0"

# --- versão ---------------------------------------------------------------
$version = [string](& (Join-Path $root "test\check-versions.ps1") -Root $root | Select-Object -Last 1)
if ([string]::IsNullOrWhiteSpace($version)) { throw "Não foi possível determinar a versão do projeto." }
$version = $version.Trim()
$tag = "v$version"

Write-Host "TaskbarTemps $version -> $tag" -ForegroundColor Cyan

# --- estado do repositório ------------------------------------------------
$branch = (& $git -C $root rev-parse --abbrev-ref HEAD).Trim()
$dirty = (& $git -C $root status --porcelain) -join "`n"
if ($dirty.Trim().Length -gt 0) {
    Write-Warning "Existem alterações não registadas:"
    Write-Host $dirty
    if (-not $DryRun) {
        throw "Registe (git commit) as alterações antes de publicar."
    }
}

$existing = (& $git -C $root tag --list $tag)
$tagExists = $existing.Trim().Length -gt 0

if ($DryRun) {
    Write-Host "  ramo: $branch"
    Write-Host "  versão: $version"
    Write-Host "  instalador: release\TaskbarTempsSetup.exe"
    Write-Host "  integridade: release\TaskbarTempsSetup.exe.sha256"
    Write-Host "  notas: $(if ($NotesFile) { $NotesFile } else { 'geradas pelo GitHub' })"
    if ($tagExists) {
        Write-Warning "A etiqueta $tag já existe: atualize a versão antes de publicar."
    }
    Write-Host "Simulação concluída (nada foi publicado)." -ForegroundColor Green
    return
}

if ($tagExists) {
    throw "A etiqueta $tag já existe. Atualize a versão antes de publicar."
}

# --- compilação -----------------------------------------------------------
& (Join-Path $root "build-setup.ps1")
if ($LASTEXITCODE -ne 0) { throw "A compilação do instalador falhou." }

$setup = Join-Path $root "release\TaskbarTempsSetup.exe"
if (-not (Test-Path $setup)) { throw "Instalador em falta: $setup" }

$hash = Join-Path $root "release\TaskbarTempsSetup.exe.sha256"
if (-not (Test-Path $hash)) { throw "Ficheiro SHA-256 em falta: $hash" }

$expectedHash = ((Get-Content $hash -Raw) -split '\s+')[0].ToLowerInvariant()
$actualHash = (Get-FileHash -Path $setup -Algorithm SHA256).Hash.ToLowerInvariant()
if ($expectedHash -ne $actualHash) {
    throw "O ficheiro SHA-256 não corresponde ao instalador compilado."
}

# A versão do instalador tem de coincidir com a etiqueta.
$info = (Get-Item $setup).VersionInfo
if ($info.ProductVersion -and $info.ProductVersion -notlike "$version*") {
    throw "O instalador compilado indica $($info.ProductVersion) em vez de $version."
}

# --- etiqueta e push ------------------------------------------------------
& $git -C $root tag -a $tag -m "TaskbarTemps $version"
if ($LASTEXITCODE -ne 0) { throw "Não foi possível criar a etiqueta $tag." }

& $git -C $root push origin $branch
if ($LASTEXITCODE -ne 0) { throw "Não foi possível enviar o ramo $branch." }

& $git -C $root push origin $tag
if ($LASTEXITCODE -ne 0) { throw "Não foi possível enviar a etiqueta $tag." }

# --- release --------------------------------------------------------------
$releaseArgs = @(
    "release", "create", $tag,
    $setup,
    $hash,
    (Join-Path $root "release\README.md"),
    "--title", "TaskbarTemps $version"
)
if ($NotesFile) {
    $releaseArgs += @("--notes-file", (Resolve-Path $NotesFile).Path)
} else {
    $releaseArgs += "--generate-notes"
}

& $gh @releaseArgs
if ($LASTEXITCODE -ne 0) { throw "Não foi possível criar a release $tag." }

Write-Host ""
Write-Host "Publicado: https://github.com/mamfo/TaskbarTemps/releases/tag/$tag" -ForegroundColor Green
