# Captura a pré-visualização (test\preview) sem precisar de elevação.
param(
    [string]$OutDir = $PSScriptRoot
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class Win32Capture {
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    public const uint LEFTDOWN = 0x0002, LEFTUP = 0x0004, RIGHTDOWN = 0x0008, RIGHTUP = 0x0010;

    public static IntPtr[] FindMenus(int processId) {
        System.Collections.Generic.List<IntPtr> found = new System.Collections.Generic.List<IntPtr>();
        EnumWindows(delegate(IntPtr h, IntPtr l) {
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            if (pid != (uint)processId) return true;
            StringBuilder cls = new StringBuilder(64);
            GetClassName(h, cls, cls.Capacity);
            if (cls.ToString() == "#32768") found.Add(h);
            return true;
        }, IntPtr.Zero);
        return found.ToArray();
    }

    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);

    // Janelas soltas do processo (menu de contexto e submenus).
    public static IntPtr[] FindPopups(int processId, IntPtr exclude) {
        System.Collections.Generic.List<IntPtr> found = new System.Collections.Generic.List<IntPtr>();
        EnumWindows(delegate(IntPtr h, IntPtr l) {
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            if (pid != (uint)processId) return true;
            if (h == exclude) return true;
            if (!IsWindowVisible(h)) return true;
            StringBuilder cls = new StringBuilder(64);
            GetClassName(h, cls, cls.Capacity);
            string name = cls.ToString();
            if (!name.StartsWith("WindowsForms10.Window", StringComparison.Ordinal)) return true;
            if (name.IndexOf("tooltips", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            RECT r;
            if (!GetWindowRect(h, out r)) return true;
            int width = r.Right - r.Left;
            int height = r.Bottom - r.Top;
            if (width >= 90 && height >= 60) found.Add(h);
            return true;
        }, IntPtr.Zero);
        return found.ToArray();
    }

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
    [void][Win32Capture]::SetCursorPos($x, $y)
    Start-Sleep -Milliseconds 250
    if ($right) {
        [void][Win32Capture]::mouse_event([Win32Capture]::RIGHTDOWN, 0, 0, 0, [UIntPtr]::Zero)
        [void][Win32Capture]::mouse_event([Win32Capture]::RIGHTUP, 0, 0, 0, [UIntPtr]::Zero)
    } else {
        [void][Win32Capture]::mouse_event([Win32Capture]::LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
        [void][Win32Capture]::mouse_event([Win32Capture]::LEFTUP, 0, 0, 0, [UIntPtr]::Zero)
    }
}

$preview = Join-Path $PSScriptRoot "preview\TaskbarTemps.exe"
$proc = Get-Process TaskbarTemps -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -eq $preview } |
    Select-Object -First 1
if (-not $proc) { throw "A pré-visualização não está em execução. Execute test\preview.ps1." }

$hwnd = [Win32Capture]::FindByTitle($proc.Id, "TaskbarTemps")
if ($hwnd -eq [IntPtr]::Zero) { throw "Janela do widget não encontrada." }

$r = New-Object Win32Capture+RECT
[void][Win32Capture]::GetWindowRect($hwnd, [ref]$r)
$w = $r.Right - $r.Left
$h = $r.Bottom - $r.Top
Write-Host "Widget: $($r.Left),$($r.Top) ${w}x${h}"

Save-Screen (Join-Path $OutDir "preview_widget.png") ($r.Left - 8) ($r.Top - 8) ($w + 16) ($h + 16)

# tooltip
[void][Win32Capture]::SetCursorPos($r.Left + [int]($w / 2), $r.Top + [int]($h / 2))
Start-Sleep -Milliseconds 300
[void][Win32Capture]::SetCursorPos($r.Left + [int]($w / 2) + 4, $r.Top + [int]($h / 2))
Start-Sleep -Seconds 2
Save-Screen (Join-Path $OutDir "preview_tooltip.png") ($r.Left - 10) ($r.Top - 220) 660 260

# menu
[System.Windows.Forms.SendKeys]::SendWait("{ESC}")
Start-Sleep -Milliseconds 300
$m = New-Object Win32Capture+RECT
$popups = @()
for ($attempt = 1; $attempt -le 3; $attempt++) {
    Click-Widget ($r.Left + [int]($w / 2)) ($r.Top + [int]($h / 2)) $true
    Start-Sleep -Seconds 1
    $popups = [Win32Capture]::FindPopups($proc.Id, $hwnd)
    if ($popups.Count -gt 0) { break }
}
if ($popups.Count -eq 0) { throw "Menu de contexto não encontrado." }
[void][Win32Capture]::GetWindowRect($popups[0], [ref]$m)
Write-Host "Menu: $($m.Left),$($m.Top) $($m.Right - $m.Left)x$($m.Bottom - $m.Top)"
Save-Screen (Join-Path $OutDir "preview_menu.png") ($m.Left - 8) ($m.Top - 8) ($m.Right - $m.Left + 16) ($m.Bottom - $m.Top + 16)

# submenu "Itens no widget" (segunda entrada do menu)
[void][Win32Capture]::SetCursorPos($m.Left + 60, $m.Top + 38)
Start-Sleep -Seconds 1
$popups = [Win32Capture]::FindPopups($proc.Id, $hwnd)
if ($popups.Count -ge 2) {
    $left = $m.Left - 8
    $top = $m.Top - 8
    $right = $m.Right
    $bottom = $m.Bottom
    foreach ($handle in $popups) {
        $rect = New-Object Win32Capture+RECT
        [void][Win32Capture]::GetWindowRect($handle, [ref]$rect)
        if ($rect.Left -ge $m.Right - 4) {
            $right = [Math]::Max($right, $rect.Right)
            $bottom = [Math]::Max($bottom, $rect.Bottom)
        }
    }
    Save-Screen (Join-Path $OutDir "preview_submenu.png") $left $top ($right - $left + 8) ($bottom - $top + 8)
} else {
    Write-Host "Submenu não detetado ($($popups.Count) janelas)."
}
[System.Windows.Forms.SendKeys]::SendWait("{ESC}")
Start-Sleep -Milliseconds 300
[System.Windows.Forms.SendKeys]::SendWait("{ESC}")
Start-Sleep -Milliseconds 400

# detalhes
Click-Widget ($r.Left + [int]($w / 2)) ($r.Top + [int]($h / 2)) $false
Start-Sleep -Seconds 2
$detailsTitle = "TaskbarTemps " + [char]0x00B7 + " sensores"
$details = [Win32Capture]::FindByTitle($proc.Id, $detailsTitle)
if ($details -ne [IntPtr]::Zero) {
    $d = New-Object Win32Capture+RECT
    [void][Win32Capture]::GetWindowRect($details, [ref]$d)
    [void][Win32Capture]::SetForegroundWindow($details)
    Start-Sleep -Milliseconds 400
    Save-Screen (Join-Path $OutDir "preview_details.png") $d.Left $d.Top ($d.Right - $d.Left) ($d.Bottom - $d.Top)
    Write-Host "Detalhes: $($d.Left),$($d.Top) $($d.Right - $d.Left)x$($d.Bottom - $d.Top)"
    [System.Windows.Forms.SendKeys]::SendWait("%{F4}")
    Start-Sleep -Milliseconds 700
} else {
    Write-Host "Janela de detalhes não encontrada."
}

Write-Host "Concluído."
