using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TaskbarTemps
{
    internal sealed class BarForm : Form
    {
        private sealed class BarItem
        {
            public string Label;
            public string Value;
            public string Slot;
            public bool IsTemperature;
            public float? Temperature;
            public float LabelWidth;
            public float SlotWidth;
            public Color Accent;
        }

        private readonly Settings _settings;
        private readonly ToolTip _tip = new ToolTip();
        private readonly List<BarItem> _items = new List<BarItem>();
        private readonly Bitmap _measureBitmap;
        private readonly Graphics _measureGraphics;
        private readonly uint _currentPid;

        private Font _labelFont;
        private Font _valueFont;
        private Palette _palette;
        private SensorSnapshot _snapshot;
        private Bitmap _surface;
        private string _lastTooltip = string.Empty;

        private int _width;
        private int _height;
        private int _x = int.MinValue;
        private int _y = int.MinValue;
        private int _defaultX;
        private int _defaultY;
        private int _taskbarHeight;
        private bool _dirty = true;
        private bool _dragging;
        private bool _pointerInside;
        private Point _dragStart;
        private int _dragWindowX;
        private int _dragWindowY;
        private DateTime _lastTopmost = DateTime.MinValue;

        private float _pad;
        private float _dot;
        private float _dotGap;
        private float _labelGap;
        private float _itemGap;

        public bool Allowed = true;
        public ContextMenuStrip PopupMenu;
        public event EventHandler DetailsRequested;

        public BarForm(Settings settings)
        {
            _settings = settings;
            _palette = ThemeManager.Create(settings.ThemeMode);
            _currentPid = (uint)Process.GetCurrentProcess().Id;

            _measureBitmap = new Bitmap(1, 1);
            _measureGraphics = Graphics.FromImage(_measureBitmap);
            _measureGraphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.Black;
            Size = new Size(1, 1);
            Text = "TaskbarTemps";

            SetStyle(ControlStyles.UserPaint
                | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.Opaque, true);

            _tip.InitialDelay = 450;
            _tip.ReshowDelay = 100;
            _tip.AutoPopDelay = 15000;
            _tip.ShowAlways = true;

            _items.Add(new BarItem { Label = "CPU", Value = "--", Slot = "99° 99%" });
            _items.Add(new BarItem { Label = "RAM", Value = "--", Slot = "99° 99%" });
            _items.Add(new BarItem { Label = "GPU", Value = "--", Slot = "99° 99%" });
            _items.Add(new BarItem { Label = "DISCO", Value = "--", Slot = "L999M E999M" });
            _items.Add(new BarItem { Label = "REDE", Value = "--", Slot = "▼999M ▲999M" });
            AttachAccents();
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_MOUSEACTIVATE)
            {
                m.Result = (IntPtr)Native.MA_NOACTIVATE;
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // desenhado através de UpdateLayeredWindow
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // desenhado através de UpdateLayeredWindow
        }

        public void SetSnapshot(SensorSnapshot snapshot, bool usageWhenNoTemp)
        {
            _snapshot = snapshot;
            if (_items.Count < 5) return;

            _items[0].Value = CombinedValue(snapshot.CpuTemp, snapshot.CpuLoad, usageWhenNoTemp);
            _items[0].IsTemperature = snapshot.CpuTemp.HasValue;
            _items[0].Temperature = snapshot.CpuTemp;

            _items[1].Value = CombinedValue(snapshot.RamTemp, snapshot.RamLoad, usageWhenNoTemp);
            _items[1].IsTemperature = snapshot.RamTemp.HasValue;
            _items[1].Temperature = snapshot.RamTemp;

            _items[2].Value = CombinedValue(snapshot.GpuTemp, snapshot.GpuLoad, usageWhenNoTemp);
            _items[2].IsTemperature = snapshot.GpuTemp.HasValue;
            _items[2].Temperature = snapshot.GpuTemp;

            if (snapshot.DiskAvailable)
            {
                _items[3].Value = "L" + DisplayFormat.ShortRate(snapshot.DiskReadBps)
                    + " E" + DisplayFormat.ShortRate(snapshot.DiskWriteBps);
            }
            else
            {
                _items[3].Value = "--";
            }
            _items[3].IsTemperature = false;
            _items[3].Temperature = null;

            if (snapshot.NetAvailable)
            {
                _items[4].Value = "▼" + DisplayFormat.ShortRate(snapshot.NetDownBps)
                    + " ▲" + DisplayFormat.ShortRate(snapshot.NetUpBps);
            }
            else
            {
                _items[4].Value = "--";
            }
            _items[4].IsTemperature = false;
            _items[4].Temperature = null;

            AttachAccents();
            _dirty = true;

            string tooltip = DisplayFormat.Tooltip(snapshot, usageWhenNoTemp);
            if (_pointerInside)
            {
                if (_lastTooltip.Length == 0) ShowTooltip();
            }
            else if (tooltip != _lastTooltip)
            {
                _lastTooltip = tooltip;
                _tip.SetToolTip(this, tooltip);
            }
        }

        private static string CombinedValue(float? temperature, float? load, bool usageWhenNoTemp)
        {
            string temp = temperature.HasValue ? DisplayFormat.ShortTemp(temperature) : null;
            string usage = load.HasValue ? DisplayFormat.ShortLoad(load) : null;

            if (temp != null && usage != null) return temp + " " + usage;
            if (temp != null) return temp;
            if (usageWhenNoTemp && usage != null) return usage;
            return "--";
        }

        public void SetPalette(Palette palette)
        {
            if (palette == null || palette.Equals(_palette)) return;
            _palette = palette;
            AttachAccents();
            _dirty = true;
        }

        private void AttachAccents()
        {
            if (_items.Count < 5 || _palette == null) return;
            _items[0].Accent = _palette.AccentCpu;
            _items[1].Accent = _palette.AccentRam;
            _items[2].Accent = _palette.AccentGpu;
            _items[3].Accent = _palette.AccentDisk;
            _items[4].Accent = _palette.AccentNet;
        }

        public void UpdatePlacement()
        {
            if (IsDisposed) return;

            if (!Allowed)
            {
                HideBar();
                return;
            }

            IntPtr tray = Native.FindWindow("Shell_TrayWnd", null);
            if (tray == IntPtr.Zero)
            {
                HideBar();
                return;
            }

            Native.RECT taskbar;
            if (!Native.GetWindowRect(tray, out taskbar) || !Native.IsWindowVisible(tray))
            {
                HideBar();
                return;
            }

            int taskbarWidth = taskbar.Width;
            int taskbarHeight = taskbar.Height;
            if (taskbarWidth < taskbarHeight)
            {
                // barra vertical: não suportado
                HideBar();
                return;
            }

            Rectangle monitor = Screen.FromHandle(tray).Bounds;
            Rectangle tb = new Rectangle(taskbar.Left, taskbar.Top, taskbarWidth, taskbarHeight);
            Rectangle intersection = Rectangle.Intersect(monitor, tb);
            if (intersection.Height < taskbarHeight / 2 || intersection.Width < taskbarWidth / 2)
            {
                // barra de tarefas escondida (ocultação automática)
                HideBar();
                return;
            }

            if (IsForegroundFullscreen())
            {
                HideBar();
                return;
            }

            // a barra pode estar coberta por uma janela em ecrã inteiro cujo processo
            // não está em primeiro plano (por exemplo, com a janela de detalhes aberta)
            IntPtr probe = Native.WindowFromPoint(
                new Native.POINT(taskbar.Right - 80, taskbar.Top + taskbarHeight / 2));
            IntPtr probeRoot = Native.GetAncestor(probe, Native.GA_ROOT);
            if (probeRoot != IntPtr.Zero && probeRoot != Handle && !Native.IsShellWindow(probeRoot))
            {
                HideBar();
                return;
            }

            UpdateMetrics(taskbarHeight);

            int anchor;
            Native.RECT notify;
            IntPtr trayNotify = Native.FindWindowEx(tray, IntPtr.Zero, "TrayNotifyWnd", null);
            if (trayNotify != IntPtr.Zero
                && Native.GetWindowRect(trayNotify, out notify)
                && notify.Width > 40)
            {
                anchor = notify.Left;
            }
            else
            {
                anchor = taskbar.Right - 300;
            }

            _defaultX = anchor - _width - 6;
            _defaultY = taskbar.Top + (taskbarHeight - _height) / 2;

            int x = _defaultX + _settings.OffsetX;
            int y = _defaultY + _settings.OffsetY;

            int minX = taskbar.Left + 4;
            int maxX = taskbar.Right - _width - 4;
            if (x < minX) x = minX;
            if (x > maxX) x = maxX;

            int minY = taskbar.Top + 1;
            int maxY = taskbar.Bottom - _height - 1;
            if (y < minY) y = minY;
            if (y > maxY) y = maxY;

            bool moved = x != _x || y != _y;
            _x = x;
            _y = y;

            if (!Visible)
            {
                Bounds = new Rectangle(x, y, _width, _height);
                Render();
                _dirty = false;
                Show();
                ApplySurface(x, y);
            }
            else
            {
                if (_dirty || _surface == null)
                {
                    Render();
                    _dirty = false;
                }
                else if (moved)
                {
                    ApplySurface(x, y);
                }
            }

            if (DateTime.Now - _lastTopmost > TimeSpan.FromSeconds(3))
            {
                Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                    Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
                _lastTopmost = DateTime.Now;
            }
        }

        public void HideTooltip()
        {
            _pointerInside = false;
            _lastTooltip = string.Empty;
            _tip.Hide(this);
        }

        private void HideBar()
        {
            if (_tip != null) _tip.Hide(this);
            if (Visible) Hide();
        }

        private bool IsForegroundFullscreen()
        {
            IntPtr foreground = Native.GetForegroundWindow();
            if (foreground == IntPtr.Zero || foreground == Handle) return false;

            uint pid;
            Native.GetWindowThreadProcessId(foreground, out pid);
            if (pid == _currentPid) return false;
            if (Native.IsShellWindow(foreground)) return false;

            Native.RECT r;
            if (!Native.GetWindowRect(foreground, out r)) return false;

            Rectangle screen = Screen.FromHandle(foreground).Bounds;
            return r.Left <= screen.Left + 2
                && r.Top <= screen.Top + 2
                && r.Right >= screen.Right - 2
                && r.Bottom >= screen.Bottom - 2;
        }

        private void UpdateMetrics(int taskbarHeight)
        {
            if (taskbarHeight == _taskbarHeight && _valueFont != null) return;
            _taskbarHeight = taskbarHeight;

            int newHeight = Math.Max(26, taskbarHeight - 8);
            float scale = newHeight / 40f;
            int valuePx = Math.Max(12, (int)Math.Round(15 * scale));
            int labelPx = Math.Max(9, (int)Math.Round(11 * scale));

            if (_labelFont != null) _labelFont.Dispose();
            if (_valueFont != null) _valueFont.Dispose();
            _labelFont = new Font("Segoe UI", labelPx, FontStyle.Regular, GraphicsUnit.Pixel);
            _valueFont = CreateValueFont(valuePx);

            _dot = Math.Max(4f, (float)Math.Round(6f * scale));
            _dotGap = Math.Max(4f, (float)Math.Round(5f * scale));
            _labelGap = 4f;
            _itemGap = Math.Max(6f, (float)Math.Round(10f * scale));
            _pad = Math.Max(6f, (float)Math.Round(9f * scale));
            _height = newHeight;

            using (StringFormat sf = CreateStringFormat())
            {
                float total = _pad * 2f + 2f;
                foreach (BarItem item in _items)
                {
                    item.LabelWidth = Measure(item.Label, _labelFont, sf).Width;
                    item.SlotWidth = Measure(item.Slot, _valueFont, sf).Width + 3f;
                    total += _dot + _dotGap + item.LabelWidth + _labelGap + item.SlotWidth;
                }
                if (_items.Count > 1) total += _itemGap * (_items.Count - 1);
                _width = (int)Math.Ceiling(total);
            }

            _dirty = true;
        }

        private static Font CreateValueFont(int pixelSize)
        {
            try
            {
                return new Font("Segoe UI Semibold", pixelSize, FontStyle.Regular, GraphicsUnit.Pixel);
            }
            catch
            {
                return new Font("Segoe UI", pixelSize, FontStyle.Bold, GraphicsUnit.Pixel);
            }
        }

        private static StringFormat CreateStringFormat()
        {
            StringFormat sf = (StringFormat)StringFormat.GenericTypographic.Clone();
            sf.FormatFlags |= StringFormatFlags.NoWrap;
            sf.Alignment = StringAlignment.Near;
            sf.LineAlignment = StringAlignment.Center;
            return sf;
        }

        private SizeF Measure(string text, Font font, StringFormat format)
        {
            return _measureGraphics.MeasureString(text, font, new PointF(0f, 0f), format);
        }

        private void Render()
        {
            if (_width <= 0 || _height <= 0 || _items.Count == 0) return;

            Bitmap bitmap = new Bitmap(_width, _height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);

                RectangleF pill = new RectangleF(1.5f, 1.5f, _width - 3.5f, _height - 3.5f);
                float radius = Math.Min(9f, (_height - 3f) / 2f);

                // Fundo praticamente transparente: o widget fica sempre com a cor
                // da barra de tarefas. O alfa mínimo (2/255) mantém a janela
                // sensível ao rato para arrastar e mostrar o tooltip.
                using (GraphicsPath path = RoundedRect(pill, radius))
                {
                    using (SolidBrush background = new SolidBrush(Color.FromArgb(2, 0, 0, 0)))
                    {
                        g.FillPath(background, path);
                    }
                }

                using (StringFormat sf = CreateStringFormat())
                {
                    float x = _pad + 1f;
                    foreach (BarItem item in _items)
                    {
                        float dotCx = x + _dot / 2f;
                        float dotCy = _height / 2f;
                        using (SolidBrush dotBrush = new SolidBrush(item.Accent))
                        {
                            g.FillEllipse(dotBrush, dotCx - _dot / 2f, dotCy - _dot / 2f, _dot, _dot);
                        }
                        x += _dot + _dotGap;

                        using (SolidBrush labelBrush = new SolidBrush(_palette.Label))
                        {
                            RectangleF labelRect = new RectangleF(x, 0f, item.LabelWidth + 4f, _height);
                            g.DrawString(item.Label, _labelFont, labelBrush, labelRect, sf);
                        }
                        x += item.LabelWidth + _labelGap;

                        Color valueColor = _palette.Value;
                        if (item.IsTemperature && item.Temperature.HasValue)
                        {
                            if (item.Temperature.Value >= 85f) valueColor = _palette.Hot;
                            else if (item.Temperature.Value >= 72f) valueColor = _palette.Warn;
                        }

                        using (SolidBrush valueBrush = new SolidBrush(valueColor))
                        {
                            RectangleF valueRect = new RectangleF(x, 0f, item.SlotWidth + 6f, _height);
                            g.DrawString(item.Value, _valueFont, valueBrush, valueRect, sf);
                        }
                        x += item.SlotWidth + _itemGap;
                    }
                }
            }

            if (_surface != null) _surface.Dispose();
            _surface = bitmap;

            ApplySurface(_x, _y);
            _dirty = false;
        }

        private static GraphicsPath RoundedRect(RectangleF rect, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float d = radius * 2f;
            path.AddArc(rect.Left, rect.Top, d, d, 180f, 90f);
            path.AddArc(rect.Right - d, rect.Top, d, d, 270f, 90f);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0f, 90f);
            path.AddArc(rect.Left, rect.Bottom - d, d, d, 90f, 90f);
            path.CloseFigure();
            return path;
        }

        private void ApplySurface(int x, int y)
        {
            if (_surface == null || IsDisposed) return;

            IntPtr hwnd = Handle;
            IntPtr screenDc = Native.GetDC(IntPtr.Zero);
            IntPtr memDc = Native.CreateCompatibleDC(screenDc);
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero;

            try
            {
                hBitmap = _surface.GetHbitmap(Color.FromArgb(0));
                oldBitmap = Native.SelectObject(memDc, hBitmap);

                Native.SIZE size = new Native.SIZE(_surface.Width, _surface.Height);
                Native.POINT source = new Native.POINT(0, 0);
                Native.POINT destination = new Native.POINT(x, y);
                Native.BLENDFUNCTION blend = new Native.BLENDFUNCTION();
                blend.BlendOp = Native.AC_SRC_OVER;
                blend.BlendFlags = 0;
                blend.SourceConstantAlpha = 255;
                blend.AlphaFormat = Native.AC_SRC_ALPHA;

                bool updated = Native.UpdateLayeredWindow(hwnd, screenDc, ref destination, ref size,
                    memDc, ref source, 0, ref blend, Native.ULW_ALPHA);
                if (!updated)
                {
                    int error = Marshal.GetLastWin32Error();
                    Log.Write("UpdateLayeredWindow falhou (erro " + error + ")");
                }
            }
            finally
            {
                if (oldBitmap != IntPtr.Zero) Native.SelectObject(memDc, oldBitmap);
                if (hBitmap != IntPtr.Zero) Native.DeleteObject(hBitmap);
                if (memDc != IntPtr.Zero) Native.DeleteDC(memDc);
                if (screenDc != IntPtr.Zero) Native.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _pointerInside = true;
            ShowTooltip();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _pointerInside = false;
            _tip.Hide(this);
        }

        private void ShowTooltip()
        {
            if (_snapshot == null) return;

            string text = DisplayFormat.Tooltip(_snapshot, _settings.UsageWhenNoTemp);
            _lastTooltip = text;
            _tip.SetToolTip(this, text);

            int x = Math.Max(0, (Width - 430) / 2);
            int y = Top > Screen.FromControl(this).Bounds.Height / 2
                ? -124
                : Height + 8;
            _tip.Show(text, this, new Point(x, y), 15000);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                _dragStart = Cursor.Position;
                _dragWindowX = _x;
                _dragWindowY = _y;
                _dragging = false;
                Capture = true;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!Capture || (Control.MouseButtons & MouseButtons.Left) == 0) return;

            Point p = Cursor.Position;
            int dx = p.X - _dragStart.X;
            int dy = p.Y - _dragStart.Y;
            if (!_dragging && Math.Abs(dx) <= 3 && Math.Abs(dy) <= 3) return;
            _dragging = true;

            int nx = _dragWindowX + dx;
            int ny = _dragWindowY + dy;
            ClampToTaskbar(ref nx, ref ny);
            _x = nx;
            _y = ny;
            ApplySurface(nx, ny);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            if (e.Button == MouseButtons.Left)
            {
                Capture = false;
                if (_dragging)
                {
                    _settings.OffsetX = _x - _defaultX;
                    _settings.OffsetY = _y - _defaultY;
                    _settings.Save();
                    _dragging = false;
                }
                else if (DetailsRequested != null)
                {
                    DetailsRequested(this, EventArgs.Empty);
                }
            }
            else if (e.Button == MouseButtons.Right)
            {
                _tip.Hide(this);
                if (PopupMenu != null) PopupMenu.Show(Cursor.Position);
            }
        }

        private void ClampToTaskbar(ref int x, ref int y)
        {
            IntPtr tray = Native.FindWindow("Shell_TrayWnd", null);
            if (tray == IntPtr.Zero) return;
            Native.RECT r;
            if (!Native.GetWindowRect(tray, out r)) return;

            int minX = r.Left + 2;
            int maxX = r.Right - _width - 2;
            int minY = r.Top + 1;
            int maxY = r.Bottom - _height - 1;
            if (x < minX) x = minX;
            if (x > maxX) x = maxX;
            if (y < minY) y = minY;
            if (y > maxY) y = maxY;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _tip.Dispose();
                if (_surface != null)
                {
                    _surface.Dispose();
                    _surface = null;
                }
                if (_valueFont != null) _valueFont.Dispose();
                if (_labelFont != null) _labelFont.Dispose();
                _measureGraphics.Dispose();
                _measureBitmap.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
