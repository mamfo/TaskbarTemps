#Requires -Version 5.1
# Gera assets/app.ico (múltiplos tamanhos, PNG comprimido) sem dependências externas.
param(
    [string]$Out = (Join-Path $PSScriptRoot '..\assets\app.ico')
)

Add-Type -AssemblyName System.Drawing

$outDir = Split-Path -Parent $Out
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }

function New-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    if ($r -le 0.1) { $p.AddRectangle((New-Object System.Drawing.RectangleF $x, $y, $w, $h)); return $p }
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Draw-Icon([System.Drawing.Graphics]$g, [int]$s) {
    $u = $s / 32.0
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

    # Fundo: quadrado arredondado com gradiente azul -> violeta
    $bg = New-RoundedPath 1 1 ($s - 2) ($s - 2) (7 * $u)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.RectangleF 0, 0, $s, $s),
        [System.Drawing.Color]::FromArgb(255, 37, 99, 235),
        [System.Drawing.Color]::FromArgb(255, 124, 58, 237),
        90.0)
    $g.FillPath($brush, $bg)
    $brush.Dispose()
    $bg.Dispose()

    # Termómetro: vidro branco
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $stemW = 7.2 * $u
    $stemX = ($s - $stemW) / 2.0
    $stemTop = 5.5 * $u
    $stemBottom = 21.5 * $u
    $stem = New-RoundedPath $stemX $stemTop $stemW ($stemBottom - $stemTop) ($stemW / 2.0)
    $g.FillPath($white, $stem)
    $stem.Dispose()

    $bulbR = 6.4 * $u
    $cx = $s / 2.0
    $cy = 22.4 * $u
    $g.FillEllipse($white, [float]($cx - $bulbR), [float]($cy - $bulbR), [float]($bulbR * 2), [float]($bulbR * 2))
    $white.Dispose()

    # Líquido vermelho
    $red = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 251, 72, 96))
    $mw = 3.4 * $u
    $mx = ($s - $mw) / 2.0
    $my = 12.0 * $u
    $merc = New-RoundedPath $mx $my $mw (10.8 * $u) ($mw / 2.0)
    $g.FillPath($red, $merc)
    $merc.Dispose()
    $mr = 3.9 * $u
    $g.FillEllipse($red, [float]($cx - $mr), [float]($cy - $mr), [float]($mr * 2), [float]($mr * 2))
    $red.Dispose()

    # Marcações de escala
    if ($s -ge 32) {
        $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(190, 255, 255, 255)), ([float](1.7 * $u))
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        foreach ($yy in 8.5, 12.5, 16.5) {
            $g.DrawLine($pen, [float](20.5 * $u), [float]($yy * $u), [float](24 * $u), [float]($yy * $u))
        }
        $pen.Dispose()
    }
}

function New-IconPng([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bmp.SetResolution(96, 96)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::Transparent)
    Draw-Icon $g $s
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ,$ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$entries = @()
foreach ($s in $sizes) {
    $entries += ,@($s, (New-IconPng $s))
}

# Preview em PNG para inspeção
[System.IO.File]::WriteAllBytes((Join-Path $outDir 'icon-preview.png'), (New-IconPng 256))

$fs = [System.IO.File]::Create($Out)
$bw = New-Object System.IO.BinaryWriter $fs
try {
    $bw.Write([UInt16]0)          # reservado
    $bw.Write([UInt16]1)          # tipo: ícone
    $bw.Write([UInt16]$entries.Count)

    $offset = 6 + 16 * $entries.Count
    foreach ($e in $entries) {
        $s = [int]$e[0]
        $data = $e[1]
        $dim = if ($s -ge 256) { 0 } else { $s }
        $bw.Write([byte]$dim)
        $bw.Write([byte]$dim)
        $bw.Write([byte]0)         # paleta
        $bw.Write([byte]0)         # reservado
        $bw.Write([UInt16]1)       # planos
        $bw.Write([UInt16]32)      # bits por pixel
        $bw.Write([UInt32]$data.Length)
        $bw.Write([UInt32]$offset)
        $offset += $data.Length
    }
    foreach ($e in $entries) {
        $bw.Write([byte[]]$e[1])
    }
} finally {
    $bw.Dispose()
    $fs.Dispose()
}

Write-Host ("Ícone criado: {0} ({1} bytes, {2} tamanhos)" -f (Resolve-Path $Out), (Get-Item $Out).Length, $entries.Count)
