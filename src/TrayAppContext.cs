using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace TaskbarTemps
{
    internal sealed class TrayAppContext : ApplicationContext
    {
        private readonly Settings _settings;
        private readonly HardwareMonitor _monitor;
        private readonly BarForm _bar;
        private readonly NotifyIcon _tray;
        private readonly ContextMenuStrip _menu;
        private readonly Timer _timer;
        private readonly Icon _icon;
        private readonly bool _ownsIcon;

        private DetailsForm _details;
        private SensorSnapshot _applied;
        private DateTime _lastThemeCheck = DateTime.MinValue;
        private bool _exiting;

        private readonly DateTime _started = DateTime.Now;
        private DateTime _lastUpdateCheck = DateTime.MinValue;
        private UpdateInfo _update;
        private bool _checkingUpdate;
        private bool _downloadingUpdate;

        private ToolStripMenuItem _miShow;
        private ToolStripMenuItem _miUsage;
        private ToolStripMenuItem _miStartup;
        private ToolStripMenuItem _miInterval1;
        private ToolStripMenuItem _miInterval2;
        private ToolStripMenuItem _miInterval5;
        private ToolStripMenuItem _miThemeAuto;
        private ToolStripMenuItem _miThemeLight;
        private ToolStripMenuItem _miThemeDark;
        private ToolStripMenuItem _miUpdate;
        private ToolStripMenuItem _miAutoUpdate;

        public TrayAppContext()
        {
            _settings = Settings.Load();
            _icon = LoadIcon(out _ownsIcon);

            _monitor = new HardwareMonitor();
            _monitor.SetInterval(_settings.IntervalMs);

            _bar = new BarForm(_settings);
            _menu = BuildMenu();
            _bar.PopupMenu = _menu;
            _bar.Allowed = _settings.Visible;
            _bar.DetailsRequested += delegate { ToggleDetails(); };

            _tray = new NotifyIcon();
            _tray.Icon = _icon;
            _tray.Text = "TaskbarTemps";
            _tray.Visible = true;
            _tray.ContextMenuStrip = _menu;
            _tray.BalloonTipClicked += delegate
            {
                if (_update != null) InstallUpdate();
            };
            _tray.MouseClick += delegate (object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left && _menu != null)
                {
                    _menu.Show(Cursor.Position);
                }
            };

            _monitor.Start();

            _timer = new Timer();
            _timer.Interval = 300;
            _timer.Tick += OnTick;
            _timer.Start();

            Log.Write("TaskbarTemps iniciado (admin=" + _monitor.Latest.IsAdmin + ")");
        }

        private static Icon LoadIcon(out bool owned)
        {
            try
            {
                Icon icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (icon != null)
                {
                    owned = true;
                    return icon;
                }
            }
            catch
            {
            }
            owned = false;
            return SystemIcons.Application;
        }

        private void OnTick(object sender, EventArgs e)
        {
            try
            {
                if (DateTime.Now - _lastThemeCheck > TimeSpan.FromSeconds(2))
                {
                    _lastThemeCheck = DateTime.Now;
                    _bar.SetPalette(ThemeManager.Create(_settings.ThemeMode));
                }

                if (_settings.CheckUpdates
                    && !_checkingUpdate
                    && !_downloadingUpdate
                    && DateTime.Now - _started > TimeSpan.FromSeconds(20)
                    && DateTime.Now - _lastUpdateCheck > TimeSpan.FromHours(6))
                {
                    BeginUpdateCheck(true);
                }

                SensorSnapshot snapshot = _monitor.Latest;
                if (!ReferenceEquals(snapshot, _applied))
                {
                    _applied = snapshot;
                    _bar.SetSnapshot(snapshot, _settings.UsageWhenNoTemp);
                    _tray.Text = DisplayFormat.TrayText(snapshot, _settings.UsageWhenNoTemp);
                    if (_details != null && _details.Visible)
                    {
                        _details.SetSnapshot(snapshot);
                    }
                }

                _bar.UpdatePlacement();
            }
            catch (Exception ex)
            {
                Log.Write("Erro no ciclo da interface: " + ex);
            }
        }

        private ContextMenuStrip BuildMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Opening += delegate { SyncMenuChecks(); };

            _miShow = new ToolStripMenuItem("Mostrar na barra de tarefas");
            _miShow.Click += delegate { SetVisible(!_settings.Visible); };
            menu.Items.Add(_miShow);

            ToolStripMenuItem details = new ToolStripMenuItem("Ver sensores…");
            details.Click += delegate { OpenDetails(); };
            menu.Items.Add(details);
            menu.Items.Add(new ToolStripSeparator());

            _miUpdate = new ToolStripMenuItem("Procurar atualizações");
            _miUpdate.Click += delegate { OnUpdateMenuClick(); };
            menu.Items.Add(_miUpdate);

            _miAutoUpdate = new ToolStripMenuItem("Procurar atualizações automaticamente");
            _miAutoUpdate.Click += delegate { ToggleAutoUpdate(); };
            menu.Items.Add(_miAutoUpdate);

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem interval = new ToolStripMenuItem("Atualizar a cada");
            _miInterval1 = new ToolStripMenuItem("1 segundo");
            _miInterval2 = new ToolStripMenuItem("2 segundos");
            _miInterval5 = new ToolStripMenuItem("5 segundos");
            _miInterval1.Click += delegate { SetInterval(1000); };
            _miInterval2.Click += delegate { SetInterval(2000); };
            _miInterval5.Click += delegate { SetInterval(5000); };
            interval.DropDownItems.Add(_miInterval1);
            interval.DropDownItems.Add(_miInterval2);
            interval.DropDownItems.Add(_miInterval5);
            menu.Items.Add(interval);

            ToolStripMenuItem theme = new ToolStripMenuItem("Tema");
            _miThemeAuto = new ToolStripMenuItem("Automático");
            _miThemeLight = new ToolStripMenuItem("Claro");
            _miThemeDark = new ToolStripMenuItem("Escuro");
            _miThemeAuto.Click += delegate { SetTheme("auto"); };
            _miThemeLight.Click += delegate { SetTheme("light"); };
            _miThemeDark.Click += delegate { SetTheme("dark"); };
            theme.DropDownItems.Add(_miThemeAuto);
            theme.DropDownItems.Add(_miThemeLight);
            theme.DropDownItems.Add(_miThemeDark);
            menu.Items.Add(theme);

            _miUsage = new ToolStripMenuItem("Mostrar uso quando não há sensor");
            _miUsage.Click += delegate { ToggleUsageFallback(); };
            menu.Items.Add(_miUsage);

            _miStartup = new ToolStripMenuItem("Iniciar com o Windows");
            _miStartup.Click += delegate { ToggleStartup(); };
            menu.Items.Add(_miStartup);

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem reset = new ToolStripMenuItem("Repor posição");
            reset.Click += delegate
            {
                _settings.OffsetX = 0;
                _settings.OffsetY = 0;
                _settings.Save();
            };
            menu.Items.Add(reset);

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem exit = new ToolStripMenuItem("Sair");
            exit.Click += delegate { ExitApplication(); };
            menu.Items.Add(exit);

            SyncMenuChecks();
            return menu;
        }

        private void SyncMenuChecks()
        {
            if (_miShow == null) return;

            _miShow.Checked = _settings.Visible;
            _miUsage.Checked = _settings.UsageWhenNoTemp;
            _miInterval1.Checked = _settings.IntervalMs == 1000;
            _miInterval2.Checked = _settings.IntervalMs == 2000;
            _miInterval5.Checked = _settings.IntervalMs == 5000;
            _miThemeAuto.Checked = _settings.ThemeMode == "auto";
            _miThemeLight.Checked = _settings.ThemeMode == "light";
            _miThemeDark.Checked = _settings.ThemeMode == "dark";
            UpdateMenuState();

            try
            {
                _miStartup.Checked = StartupManager.IsEnabled();
            }
            catch
            {
                _miStartup.Checked = false;
            }
        }

        private void SetVisible(bool visible)
        {
            _settings.Visible = visible;
            _settings.Save();
            _bar.Allowed = visible;
            if (!visible) _bar.Hide();
            SyncMenuChecks();
        }

        private void SetInterval(int milliseconds)
        {
            _settings.IntervalMs = milliseconds;
            _settings.Save();
            _monitor.SetInterval(milliseconds);
            SyncMenuChecks();
        }

        private void SetTheme(string mode)
        {
            _settings.ThemeMode = mode;
            _settings.Save();
            _bar.SetPalette(ThemeManager.Create(mode));
            SyncMenuChecks();
        }

        private void ToggleUsageFallback()
        {
            _settings.UsageWhenNoTemp = !_settings.UsageWhenNoTemp;
            _settings.Save();
            _applied = null;
            SyncMenuChecks();
        }

        private void ToggleStartup()
        {
            bool enable = !StartupManager.IsEnabled();
            string error;
            if (!StartupManager.SetEnabled(enable, out error))
            {
                string message;
                if (!_monitor.Latest.IsAdmin)
                {
                    message = "Para alterar o arranque automático, execute o TaskbarTemps como administrador.";
                }
                else
                {
                    message = "Não foi possível alterar o arranque automático.\n\n" + error;
                }

                MessageBox.Show(
                    message,
                    "TaskbarTemps",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            SyncMenuChecks();
        }

        private void ToggleDetails()
        {
            if (_details != null && _details.Visible)
            {
                _details.Close();
                return;
            }
            OpenDetails();
        }

        private void OpenDetails()
        {
            if (_details == null || _details.IsDisposed)
            {
                _details = new DetailsForm(_icon);
                _details.FormClosed += delegate { _details = null; };
            }
            _details.SetSnapshot(_monitor.Latest);
            _bar.HideTooltip();
            _details.ShowNear(_bar.Handle);
        }

        private void OnUpdateMenuClick()
        {
            if (_downloadingUpdate) return;
            if (_update != null) InstallUpdate();
            else BeginUpdateCheck(false);
        }

        private void ToggleAutoUpdate()
        {
            _settings.CheckUpdates = !_settings.CheckUpdates;
            _settings.Save();
            UpdateMenuState();
            if (_settings.CheckUpdates)
            {
                _lastUpdateCheck = DateTime.MinValue;
                BeginUpdateCheck(true);
            }
        }

        private void BeginUpdateCheck(bool silent)
        {
            if (_checkingUpdate || _downloadingUpdate) return;
            _checkingUpdate = true;
            _lastUpdateCheck = DateTime.Now;
            UpdateMenuState();

            Updater.CheckAsync(
                delegate (UpdateInfo info) { Post(delegate { CompleteUpdateCheck(info, silent); }); },
                delegate (Exception ex) { Post(delegate { FailUpdateCheck(ex, silent); }); });
        }

        private void CompleteUpdateCheck(UpdateInfo info, bool silent)
        {
            _checkingUpdate = false;
            _update = Updater.IsNewer(info) ? info : null;
            UpdateMenuState();

            if (_update != null)
            {
                string version = _update.Version.ToString(3);
                Log.Write("Nova versão disponível: " + version);
                if (silent)
                {
                    _tray.ShowBalloonTip(10000, "TaskbarTemps",
                        "Nova versão " + version + " disponível. Clique aqui para instalar.",
                        ToolTipIcon.Info);
                }
                else
                {
                    DialogResult answer = MessageBox.Show(
                        "Está disponível a versão " + version
                        + " (tem a " + Updater.InstalledText + ").\n\n"
                        + "Instalar agora? O TaskbarTemps será reiniciado.",
                        "TaskbarTemps", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                    if (answer == DialogResult.Yes) InstallUpdate();
                }
            }
            else if (!silent)
            {
                MessageBox.Show("Tem a versão mais recente (" + Updater.InstalledText + ").",
                    "TaskbarTemps", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void FailUpdateCheck(Exception ex, bool silent)
        {
            _checkingUpdate = false;
            UpdateMenuState();
            Log.Write("Falha ao procurar atualizações: " + ex.Message);
            if (silent) return;

            MessageBox.Show(
                "Não foi possível procurar atualizações.\n\n" + ex.Message
                + "\n\nPode descarregar manualmente em:\n" + Updater.ReleasePage,
                "TaskbarTemps", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void InstallUpdate()
        {
            if (_update == null || _downloadingUpdate) return;

            DialogResult answer = MessageBox.Show(
                "Instalar a versão " + _update.Version.ToString(3) + "?\n\n"
                + "O instalador substitui a versão atual e reinicia o widget.",
                "TaskbarTemps", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (answer != DialogResult.OK) return;

            _downloadingUpdate = true;
            UpdateMenuState();

            Updater.DownloadAsync(_update,
                delegate (int percent)
                {
                    Post(delegate
                    {
                        if (_miUpdate != null) _miUpdate.Text = "A descarregar… " + percent + "%";
                    });
                },
                delegate (string path) { Post(delegate { StartInstaller(path); }); },
                delegate (Exception ex) { Post(delegate { FailDownload(ex); }); });
        }

        private void StartInstaller(string path)
        {
            _downloadingUpdate = false;
            if (_update == null || !Updater.VerifyFile(path, _update.Sha256))
            {
                try { File.Delete(path); } catch { }
                UpdateMenuState();
                MessageBox.Show(
                    "O instalador descarregado não passou na verificação de integridade.\n\n"
                    + "Descarregue manualmente em:\n" + Updater.ReleasePage,
                    "TaskbarTemps", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                ProcessStartInfo info = new ProcessStartInfo(path);
                info.UseShellExecute = true;
                info.WorkingDirectory = Path.GetDirectoryName(path);
                Process.Start(info);
                ExitApplication();
            }
            catch (Exception ex)
            {
                UpdateMenuState();
                MessageBox.Show("Não foi possível iniciar o instalador:\n" + ex.Message,
                    "TaskbarTemps", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void FailDownload(Exception ex)
        {
            _downloadingUpdate = false;
            UpdateMenuState();
            MessageBox.Show(
                "Não foi possível descarregar a atualização:\n" + ex.Message
                + "\n\nPode descarregar manualmente em:\n" + Updater.ReleasePage,
                "TaskbarTemps", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void UpdateMenuState()
        {
            if (_miUpdate != null)
            {
                if (_downloadingUpdate) _miUpdate.Text = "A descarregar a atualização…";
                else if (_update != null) _miUpdate.Text = "Instalar versão " + _update.Version.ToString(3) + "…";
                else if (_checkingUpdate) _miUpdate.Text = "A procurar atualizações…";
                else _miUpdate.Text = "Procurar atualizações";
                _miUpdate.Enabled = !_checkingUpdate && !_downloadingUpdate;
            }
            if (_miAutoUpdate != null) _miAutoUpdate.Checked = _settings.CheckUpdates;
        }

        private void Post(MethodInvoker action)
        {
            try
            {
                if (_bar != null && _bar.IsHandleCreated)
                {
                    _bar.BeginInvoke(action);
                    return;
                }
            }
            catch
            {
            }
            try { action(); } catch { }
        }

        private void ExitApplication()
        {
            if (_exiting) return;
            _exiting = true;
            _settings.Save();
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_timer != null)
                {
                    _timer.Stop();
                    _timer.Dispose();
                }
                if (_details != null)
                {
                    _details.Dispose();
                    _details = null;
                }
                if (_tray != null)
                {
                    _tray.Visible = false;
                    _tray.Dispose();
                }
                if (_monitor != null) _monitor.Dispose();
                if (_bar != null) _bar.Dispose();
                if (_menu != null) _menu.Dispose();
                if (_icon != null && _ownsIcon) _icon.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
