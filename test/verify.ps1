# Verificação visual do widget, tooltip, detalhes e menu.
# Usa a build final em dist e reutiliza a instância em execução.
# Deve ser executado como administrador: o widget corre elevado e o Windows
# bloqueia cliques simulados de processos com integridade inferior.
param(
    [string]$Exe = (Join-Path (Split-Path -Parent $PSScriptRoot) "dist\TaskbarTemps.exe"),
    [string]$OutDir = $PSScriptRoot
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class Win32Test {
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    public const uint LEFTDOWN = 0x0002, LEFTUP = 0x0004, RIGHTDOWN = 0x0008, RIGHTUP = 0x0010;

    public static IntPtr FindByTitle(int processId, string title) {
        IntPtr found = IntPtr.Zero;
        EnumWindows(delegate(IntPtr h, IntPtr l) {
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            if (pid != (uint)processId) return true;
            StringBuilder sb = new StringBuilder(512);
            GetWindowText(h, sb, sb.Capacity);
            if (sb.ToString() == title) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
"@

function Save-Screen([string]$path, [int]$x, [int]$y, [int]$w, [int]$h) {
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($x, $y, 0, 0, (New-Object System.Drawing.Size $w, $h))
    $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

function Click-Widget([int]$x, [int]$y, [bool]$right) {
    [void][Win32Test]::SetCursorPos($x, $y)
    Start-Sleep -Milliseconds 250
    if ($right) {
        [void][Win32Test]::mouse_event([Win32Test]::RIGHTDOWN, 0, 0, 0, [UIntPtr]::Zero)
        [void][Win32Test]::mouse_event([Win32Test]::RIGHTUP, 0, 0, 0, [UIntPtr]::Zero)
    } else {
        [void][Win32Test]::mouse_event([Win32Test]::LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
        [void][Win32Test]::mouse_event([Win32Test]::LEFTUP, 0, 0, 0, [UIntPtr]::Zero)
    }
}

$running = @(Get-Process TaskbarTemps -ErrorAction SilentlyContinue)
if ($running.Count -eq 0) {
    [void][Win32Test]::SetCursorPos(200, 200)
    # A build final tem manifesto de administrador: pede elevação.
    Start-Process -FilePath $Exe -Verb RunAs
    Start-Sleep -Seconds 4
} else {
    Write-Host "TaskbarTemps já em execução (PID $($running[0].Id)); a usar essa instância."
}

$proc = Get-Process TaskbarTemps | Select-Object -First 1
$hwnd = [Win32Test]::FindByTitle($proc.Id, "TaskbarTemps")
if ($hwnd -eq [IntPtr]::Zero) { throw "Janela do widget nao encontrada." }
$r = New-Object Win32Test+RECT
[void][Win32Test]::GetWindowRect($hwnd, [ref]$r)
$w = $r.Right - $r.Left
$h = $r.Bottom - $r.Top
Write-Host "Widget: $($r.Left),$($r.Top) ${w}x${h}"

Save-Screen (Join-Path $OutDir "verify_widget.png") ($r.Left - 8) ($r.Top - 8) ($w + 16) ($h + 16)

# tooltip
[void][Win32Test]::SetCursorPos($r.Left + [int]($w / 2), $r.Top + [int]($h / 2))
Start-Sleep -Milliseconds 300
[void][Win32Test]::SetCursorPos($r.Left + [int]($w / 2) + 4, $r.Top + [int]($h / 2))
Start-Sleep -Seconds 2
Save-Screen (Join-Path $OutDir "verify_tooltip.png") ($r.Left - 10) ($r.Top - 200) 640 240

# details
Click-Widget ($r.Left + [int]($w / 2)) ($r.Top + [int]($h / 2)) $false
Start-Sleep -Seconds 2
$detailsTitle = "TaskbarTemps " + [char]0x00B7 + " sensores"
$details = [Win32Test]::FindByTitle($proc.Id, $detailsTitle)
if ($details -ne [IntPtr]::Zero) {
    $d = New-Object Win32Test+RECT
    [void][Win32Test]::GetWindowRect($details, [ref]$d)
    [void][Win32Test]::SetForegroundWindow($details)
    Start-Sleep -Milliseconds 300
    Save-Screen (Join-Path $OutDir "verify_details.png") $d.Left $d.Top ($d.Right - $d.Left) ($d.Bottom - $d.Top)
    Write-Host "Detalhes: $($d.Left),$($d.Top) $($d.Right - $d.Left)x$($d.Bottom - $d.Top)"
    [System.Windows.Forms.SendKeys]::SendWait("%{F4}")
    Start-Sleep -Milliseconds 700
} else {
    Write-Host "Janela de detalhes nao encontrada."
}

# menu
Click-Widget ($r.Left + [int]($w / 2)) ($r.Top + [int]($h / 2)) $true
Start-Sleep -Seconds 1
Save-Screen (Join-Path $OutDir "verify_menu.png") ($r.Left - 300) ($r.Top - 480) 620 520
[System.Windows.Forms.SendKeys]::SendWait("{ESC}")
Start-Sleep -Milliseconds 400

Write-Host "Concluido."
