using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TaskbarTempsSetup
{
    internal sealed class SetupOptions
    {
        public string Directory;
        public bool Startup = true;
        public bool Shortcut = true;
        public bool Launch = true;
        public bool Silent;

        public static SetupOptions Parse(string[] args)
        {
            SetupOptions o = new SetupOptions();
            o.Directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "TaskbarTemps");

            foreach (string arg in args)
            {
                string text = arg.Trim();
                if (text.Equals("/S", StringComparison.OrdinalIgnoreCase)
                    || text.Equals("--silent", StringComparison.OrdinalIgnoreCase))
                {
                    o.Silent = true;
                    continue;
                }
                if (text.StartsWith("/dir:", StringComparison.OrdinalIgnoreCase))
                {
                    string value = text.Substring(5);
                    if (!string.IsNullOrEmpty(value)) o.Directory = value;
                    continue;
                }
                if (text.StartsWith("--dir=", StringComparison.OrdinalIgnoreCase))
                {
                    string value = text.Substring(6);
                    if (!string.IsNullOrEmpty(value)) o.Directory = value;
                    continue;
                }
                if (text.StartsWith("/startup:", StringComparison.OrdinalIgnoreCase))
                {
                    o.Startup = text.EndsWith("1", StringComparison.Ordinal);
                    continue;
                }
                if (text.StartsWith("/shortcut:", StringComparison.OrdinalIgnoreCase))
                {
                    o.Shortcut = text.EndsWith("1", StringComparison.Ordinal);
                    continue;
                }
                if (text.StartsWith("/launch:", StringComparison.OrdinalIgnoreCase))
                {
                    o.Launch = text.EndsWith("1", StringComparison.Ordinal);
                }
            }

            o.Directory = Path.GetFullPath(o.Directory);
            return o;
        }
    }

    internal static class Installer
    {
        private const string AppName = "TaskbarTemps";
        public const string AppVersion = "1.4.0";
        public const string PawnIoVersion = "2.2.0";
        private const string PawnIoResource = "PawnIO_setup.exe";
        private const string UninstallKey =
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\TaskbarTemps";

        private static readonly List<string> Warnings = new List<string>();
        private static readonly object Gate = new object();

        /// <summary>Resumo do estado do driver, mostrado no fim da instalação.</summary>
        public static string PawnIoSummary = string.Empty;

        public static string ShortcutPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    @"Microsoft\Windows\Start Menu\Programs\TaskbarTemps.lnk");
            }
        }

        public static int Install(SetupOptions options, Action<string> status)
        {
            lock (Gate) Warnings.Clear();

            try
            {
                status("A parar instâncias em execução…");
                StopRunning();

                status("A copiar ficheiros para " + options.Directory + "…");
                ExtractPayload(options.Directory);

                // Instalações antigas deixavam o instalador do PawnIO na pasta da
                // aplicação; agora é extraído para %TEMP% e já não é necessário.
                try
                {
                    string legacy = Path.Combine(options.Directory, PawnIoResource);
                    if (File.Exists(legacy)) File.Delete(legacy);
                }
                catch
                {
                }

                status("A instalar o PawnIO " + PawnIoVersion + " (driver dos sensores)…");
                string pawnIoError;
                bool pawnIoNew;
                bool pawnIo = EnsurePawnIo(out pawnIoError, out pawnIoNew);
                if (pawnIo)
                {
                    PawnIoSummary = pawnIoNew
                        ? "PawnIO " + PawnIoVersion + " instalado."
                        : "PawnIO " + PawnIoVersion + " já estava instalado.";
                    status(pawnIoNew
                        ? "PawnIO " + PawnIoVersion + " instalado."
                        : "PawnIO " + PawnIoVersion + " já presente — ignorado.");
                }
                else
                {
                    PawnIoSummary = "PawnIO " + PawnIoVersion + " não instalado.";
                    Warnings.Add("PawnIO não instalado: " + pawnIoError);
                }

                if (options.Shortcut)
                {
                    status("A criar atalho no Menu Iniciar…");
                    try
                    {
                        CreateShortcut(options.Directory);
                    }
                    catch (Exception ex)
                    {
                        Warnings.Add("Atalho não criado: " + ex.Message);
                    }
                }

                if (options.Startup)
                {
                    status("A configurar o arranque automático…");
                    string startupError;
                    if (!RunApp(options.Directory, "--install-startup", out startupError))
                    {
                        Warnings.Add("Arranque automático não configurado: " + startupError);
                    }
                }
                else
                {
                    string unused;
                    RunApp(options.Directory, "--remove-startup", out unused);
                }

                status("A registar a desinstalação…");
                RegisterUninstall(options.Directory);

                if (options.Launch)
                {
                    status("A iniciar o TaskbarTemps…");
                    StartApp(options.Directory);
                }

                status("Instalação concluída.");
                return 0;
            }
            catch (Exception ex)
            {
                Log("ERRO: " + ex);
                lock (Gate) Warnings.Add(ex.Message);
                return 1;
            }
        }

        public static string[] TakeWarnings()
        {
            lock (Gate)
            {
                string[] result = Warnings.ToArray();
                Warnings.Clear();
                return result;
            }
        }

        private static void StopRunning()
        {
            foreach (Process process in Process.GetProcessesByName("TaskbarTemps"))
            {
                try
                {
                    process.Kill();
                    process.WaitForExit(4000);
                }
                catch
                {
                }
            }
        }

        private static void ExtractPayload(string target)
        {
            Directory.CreateDirectory(target);
            Assembly assembly = Assembly.GetExecutingAssembly();
            string[] names = assembly.GetManifestResourceNames();
            if (names.Length == 0) throw new InvalidOperationException("Pacote de instalação sem ficheiros.");

            foreach (string name in names)
            {
                // O instalador do PawnIO não fica na pasta da aplicação: é
                // extraído para %TEMP% e executado dentro da mesma instalação.
                if (string.Equals(name, PawnIoResource, StringComparison.OrdinalIgnoreCase)) continue;

                using (Stream source = assembly.GetManifestResourceStream(name))
                {
                    if (source == null) continue;
                    string path = Path.Combine(target, name);
                    string directory = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                    using (FileStream destination = File.Create(path))
                    {
                        source.CopyTo(destination);
                    }
                }
            }
        }

        private static bool EnsurePawnIo(out string error, out bool installedNow)
        {
            error = string.Empty;
            installedNow = false;

            if (IsPawnIoInstalled())
            {
                return true;
            }

            string setup = ExtractPawnIo();
            if (setup == null)
            {
                error = "instalador do PawnIO em falta no pacote";
                return false;
            }

            try
            {
                ProcessStartInfo info = new ProcessStartInfo(setup, "-install -silent");
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                info.WorkingDirectory = Path.GetTempPath();

                using (Process process = Process.Start(info))
                {
                    if (!process.WaitForExit(180000))
                    {
                        try { process.Kill(); } catch { }
                        error = "o instalador não terminou a tempo";
                        return false;
                    }

                    // 0 = sucesso, 3010 = sucesso com reinício pendente.
                    if (process.ExitCode != 0 && process.ExitCode != 3010 && !IsPawnIoInstalled())
                    {
                        error = "o instalador devolveu o código " + process.ExitCode;
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                try { File.Delete(setup); } catch { }
            }

            if (!IsPawnIoInstalled())
            {
                error = "o serviço PawnIO não ficou disponível";
                return false;
            }

            installedNow = true;
            return true;
        }

        /// <summary>Extrai o instalador do PawnIO (recurso incorporado) para %TEMP%.</summary>
        private static string ExtractPawnIo()
        {
            try
            {
                Assembly assembly = Assembly.GetExecutingAssembly();
                using (Stream source = assembly.GetManifestResourceStream(PawnIoResource))
                {
                    if (source == null) return null;
                    string path = Path.Combine(Path.GetTempPath(), "TaskbarTemps_PawnIO_setup.exe");
                    using (FileStream destination = File.Create(path))
                    {
                        source.CopyTo(destination);
                    }
                    return path;
                }
            }
            catch
            {
                return null;
            }
        }

        private static bool IsPawnIoInstalled()
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Services\PawnIO"))
                {
                    return key != null;
                }
            }
            catch
            {
                return false;
            }
        }

        private static void CreateShortcut(string target)
        {
            string exe = Path.Combine(target, "TaskbarTemps.exe");
            string path = ShortcutPath;
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) throw new InvalidOperationException("WScript.Shell indisponível.");

            object shell = Activator.CreateInstance(shellType);
            object shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod,
                null, shell, new object[] { path });

            Type shortcutType = shortcut.GetType();
            shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { exe });
            shortcutType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { target });
            shortcutType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { exe + ",0" });
            shortcutType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut,
                new object[] { "Temperaturas e rede na barra de tarefas" });
            shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
        }

        private static bool RunApp(string target, string arguments, out string error)
        {
            error = string.Empty;
            string exe = Path.Combine(target, "TaskbarTemps.exe");
            if (!File.Exists(exe))
            {
                error = "TaskbarTemps.exe em falta";
                return false;
            }

            try
            {
                ProcessStartInfo info = new ProcessStartInfo(exe, arguments);
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                info.WorkingDirectory = target;
                using (Process process = Process.Start(info))
                {
                    if (!process.WaitForExit(30000))
                    {
                        try { process.Kill(); } catch { }
                        error = "tempo excedido";
                        return false;
                    }
                    if (process.ExitCode != 0)
                    {
                        error = "código " + process.ExitCode;
                        return false;
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static void StartApp(string target)
        {
            ProcessStartInfo info = new ProcessStartInfo(Path.Combine(target, "TaskbarTemps.exe"));
            info.WorkingDirectory = target;
            info.UseShellExecute = true;
            Process.Start(info);
        }

        private static void RegisterUninstall(string target)
        {
            string exe = Path.Combine(target, "TaskbarTemps.exe");
            string version = AppVersion;
            try
            {
                version = FileVersionInfo.GetVersionInfo(exe).FileVersion;
            }
            catch
            {
            }

            long sizeKb = 0;
            try
            {
                foreach (string file in Directory.GetFiles(target))
                {
                    sizeKb += new FileInfo(file).Length / 1024;
                }
            }
            catch
            {
            }

            using (RegistryKey key = Registry.LocalMachine.CreateSubKey(UninstallKey))
            {
                key.SetValue("DisplayName", AppName);
                key.SetValue("DisplayVersion", version);
                key.SetValue("Publisher", "mamfo.net");
                key.SetValue("URLInfoAbout", "https://www.mamfo.net");
                key.SetValue("InstallLocation", target);
                key.SetValue("DisplayIcon", exe + ",0");
                key.SetValue("UninstallString", "\"" + exe + "\" --uninstall");
                key.SetValue("QuietUninstallString", "\"" + exe + "\" --uninstall");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                key.SetValue("EstimatedSize", (int)sizeKb, RegistryValueKind.DWord);
            }
        }

        public static void Log(string message)
        {
            try
            {
                File.AppendAllText(
                    Path.Combine(Path.GetTempPath(), "TaskbarTempsSetup.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine);
            }
            catch
            {
            }
        }
    }

    internal sealed class SetupForm : Form
    {
        private readonly SetupOptions _options;
        private readonly Label _status;
        private readonly CheckBox _startup;
        private readonly CheckBox _shortcut;
        private readonly Button _install;
        private readonly Button _cancel;
        private readonly ProgressBar _progress;

        public SetupForm(SetupOptions options)
        {
            _options = options;

            Text = "TaskbarTemps " + Installer.AppVersion + " — Instalação";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            AutoScaleMode = AutoScaleMode.Font;
            Font = new Font("Segoe UI", 9f);
            BackColor = Color.White;
            ClientSize = new Size(460, 334);

            Label title = new Label();
            title.Text = "TaskbarTemps";
            title.Font = new Font("Segoe UI Semibold", 16f);
            title.ForeColor = Color.FromArgb(28, 32, 40);
            title.Location = new Point(22, 18);
            title.AutoSize = true;

            Label subtitle = new Label();
            subtitle.Text = "Widget, sensores e driver PawnIO numa única instalação.";
            subtitle.ForeColor = Color.FromArgb(96, 102, 112);
            subtitle.Location = new Point(24, 56);
            subtitle.AutoSize = true;

            Label components = new Label();
            components.Text = "TaskbarTemps " + Installer.AppVersion
                + "  ·  PawnIO " + Installer.PawnIoVersion + "  ·  Windows 11";
            components.ForeColor = Color.FromArgb(120, 126, 136);
            components.Location = new Point(24, 80);
            components.AutoSize = true;

            Label destination = new Label();
            destination.Text = "Destino: " + options.Directory;
            destination.ForeColor = Color.FromArgb(120, 126, 136);
            destination.Location = new Point(24, 104);
            destination.Size = new Size(412, 18);
            destination.AutoEllipsis = true;

            _startup = new CheckBox();
            _startup.Text = "Iniciar com o Windows";
            _startup.Checked = options.Startup;
            _startup.Location = new Point(24, 136);
            _startup.AutoSize = true;

            _shortcut = new CheckBox();
            _shortcut.Text = "Criar atalho no Menu Iniciar";
            _shortcut.Checked = options.Shortcut;
            _shortcut.Location = new Point(24, 162);
            _shortcut.AutoSize = true;

            _status = new Label();
            _status.Text = "Pronto para instalar.";
            _status.ForeColor = Color.FromArgb(96, 102, 112);
            _status.Location = new Point(24, 198);
            _status.Size = new Size(412, 18);
            _status.AutoEllipsis = true;

            _progress = new ProgressBar();
            _progress.Location = new Point(24, 222);
            _progress.Size = new Size(412, 6);
            _progress.Style = ProgressBarStyle.Marquee;
            _progress.MarqueeAnimationSpeed = 0;
            _progress.Visible = false;

            _install = new Button();
            _install.Text = "Instalar";
            _install.Size = new Size(110, 32);
            _install.Location = new Point(234, 282);
            _install.FlatStyle = FlatStyle.System;
            _install.Click += delegate { BeginInstall(); };

            _cancel = new Button();
            _cancel.Text = "Cancelar";
            _cancel.Size = new Size(92, 32);
            _cancel.Location = new Point(352, 282);
            _cancel.FlatStyle = FlatStyle.System;
            _cancel.Click += delegate { Close(); };

            AcceptButton = _install;
            CancelButton = _cancel;

            LinkLabel credit = new LinkLabel();
            credit.Text = "Desenvolvido por mamfo.net";
            credit.LinkArea = new LinkArea(17, 9);
            credit.AutoSize = true;
            credit.Location = new Point(26, 291);
            credit.LinkColor = Color.FromArgb(37, 99, 235);
            credit.ActiveLinkColor = Color.FromArgb(29, 78, 216);
            credit.LinkClicked += delegate { OpenUrl("https://www.mamfo.net"); };

            Controls.Add(title);
            Controls.Add(subtitle);
            Controls.Add(destination);
            Controls.Add(components);
            Controls.Add(_startup);
            Controls.Add(_shortcut);
            Controls.Add(_status);
            Controls.Add(_progress);
            Controls.Add(_install);
            Controls.Add(_cancel);
            Controls.Add(credit);
        }

        private static void OpenUrl(string url)
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo(url);
                info.UseShellExecute = true;
                Process.Start(info);
            }
            catch
            {
            }
        }

        private void BeginInstall()
        {
            _options.Startup = _startup.Checked;
            _options.Shortcut = _shortcut.Checked;

            _install.Enabled = false;
            _cancel.Enabled = false;
            _startup.Enabled = false;
            _shortcut.Enabled = false;
            _progress.Visible = true;
            _progress.MarqueeAnimationSpeed = 30;
            SetStatus("A instalar…");

            System.Threading.Thread thread = new System.Threading.Thread(delegate ()
            {
                int code = Installer.Install(_options, ReportStatus);
                try
                {
                    BeginInvoke((MethodInvoker)delegate { Finish(code); });
                }
                catch
                {
                }
            });
            thread.IsBackground = true;
            thread.Start();
        }

        private void ReportStatus(string text)
        {
            try
            {
                BeginInvoke((MethodInvoker)delegate { SetStatus(text); });
            }
            catch
            {
            }
        }

        private void SetStatus(string text)
        {
            if (IsDisposed) return;
            _status.Text = text;
        }

        private void Finish(int code)
        {
            _progress.MarqueeAnimationSpeed = 0;
            _progress.Visible = false;
            _cancel.Enabled = true;
            _cancel.Text = "Fechar";

            string[] warnings = Installer.TakeWarnings();
            if (code == 0)
            {
                string message = "TaskbarTemps " + Installer.AppVersion + " instalado em:\n"
                    + _options.Directory;
                if (!string.IsNullOrEmpty(Installer.PawnIoSummary))
                {
                    message += "\n\n" + Installer.PawnIoSummary;
                }
                if (warnings.Length > 0) message += "\n\nAvisos:\n• " + string.Join("\n• ", warnings);
                SetStatus("Instalação concluída.");
                MessageBox.Show(this, message, "TaskbarTemps", MessageBoxButtons.OK,
                    warnings.Length > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
                Close();
            }
            else
            {
                string message = warnings.Length > 0
                    ? string.Join("\n", warnings)
                    : "Não foi possível concluir a instalação.";
                SetStatus("A instalação falhou.");
                MessageBox.Show(this, message, "TaskbarTemps", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _install.Enabled = true;
                _startup.Enabled = true;
                _shortcut.Enabled = true;
            }
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            SetupOptions options = SetupOptions.Parse(args);

            if (options.Silent)
            {
                int code = Installer.Install(options, Installer.Log);
                Installer.Log("Resultado: " + code);
                Environment.ExitCode = code;
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm(options));
        }
    }
}
