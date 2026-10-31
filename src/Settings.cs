using System;
using System.Drawing;
using System.Globalization;
using System.IO;

namespace TaskbarTemps
{
    internal sealed class Settings
    {
        public int IntervalMs = 1000;
        public bool Visible = true;
        public int OffsetX;
        public int OffsetY;
        public string ThemeMode = "auto";
        public bool UsageWhenNoTemp = true;
        public bool CheckUpdates = true;

        /// <summary>Descarrega e instala a versão nova sem perguntar.</summary>
        public bool AutoInstall = true;

        // Itens visíveis no widget
        public bool ShowCpu = true;
        public bool ShowRam = true;
        public bool ShowGpu = true;
        public bool ShowDisk = true;
        public bool ShowNet = true;
        public bool ShowIpLocal;
        public bool ShowIpPublic;

        public static string DataDirectory
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskbarTemps"); }
        }

        public static string FilePath
        {
            get
            {
                // As pré-visualizações de teste podem apontar para um ficheiro
                // próprio, para nunca mexerem nas definições da aplicação
                // instalada.
                string isolated = Environment.GetEnvironmentVariable("TBT_SETTINGS_FILE");
                if (!string.IsNullOrEmpty(isolated)) return isolated;
                return Path.Combine(DataDirectory, "settings.ini");
            }
        }

        public static Settings Load()
        {
            Settings s = new Settings();
            string text = ReadSettingsText();
            if (text == null) return s;
            try
            {
                foreach (string raw in text.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string value = line.Substring(eq + 1).Trim();
                    switch (key)
                    {
                        case "intervalms":
                            {
                                int v;
                                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
                                    s.IntervalMs = Math.Max(500, Math.Min(10000, v));
                                break;
                            }
                        case "visible":
                            {
                                bool v;
                                if (bool.TryParse(value, out v)) s.Visible = v;
                                break;
                            }
                        case "offsetx":
                            {
                                int v;
                                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) s.OffsetX = v;
                                break;
                            }
                        case "offsety":
                            {
                                int v;
                                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) s.OffsetY = v;
                                break;
                            }
                        case "theme":
                            if (value == "light" || value == "dark" || value == "auto") s.ThemeMode = value;
                            break;
                        case "usagewhennotemp":
                            {
                                bool v;
                                if (bool.TryParse(value, out v)) s.UsageWhenNoTemp = v;
                                break;
                            }
                        case "checkupdates":
                            {
                                bool v;
                                if (bool.TryParse(value, out v)) s.CheckUpdates = v;
                                break;
                            }
                        case "autoinstall":
                            s.AutoInstall = ReadBool(value, s.AutoInstall);
                            break;
                        case "showcpu":
                            s.ShowCpu = ReadBool(value, s.ShowCpu);
                            break;
                        case "showram":
                            s.ShowRam = ReadBool(value, s.ShowRam);
                            break;
                        case "showgpu":
                            s.ShowGpu = ReadBool(value, s.ShowGpu);
                            break;
                        case "showdisk":
                            s.ShowDisk = ReadBool(value, s.ShowDisk);
                            break;
                        case "shownet":
                            s.ShowNet = ReadBool(value, s.ShowNet);
                            break;
                        case "showiplocal":
                            s.ShowIpLocal = ReadBool(value, s.ShowIpLocal);
                            break;
                        case "showippublic":
                            s.ShowIpPublic = ReadBool(value, s.ShowIpPublic);
                            break;
                    }
                }
                Log.Write("Definições carregadas: " + Describe(s));
            }
            catch (Exception ex)
            {
                Log.Write("Falha ao ler configurações: " + ex.Message);
            }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                string[] lines = new string[]
                {
                    "# TaskbarTemps - configuração",
                    "IntervalMs=" + IntervalMs.ToString(CultureInfo.InvariantCulture),
                    "Visible=" + Visible.ToString(),
                    "OffsetX=" + OffsetX.ToString(CultureInfo.InvariantCulture),
                    "OffsetY=" + OffsetY.ToString(CultureInfo.InvariantCulture),
                    "Theme=" + ThemeMode,
                    "UsageWhenNoTemp=" + UsageWhenNoTemp.ToString(),
                    "CheckUpdates=" + CheckUpdates.ToString(),
                    "AutoInstall=" + AutoInstall.ToString(),
                    "ShowCpu=" + ShowCpu.ToString(),
                    "ShowRam=" + ShowRam.ToString(),
                    "ShowGpu=" + ShowGpu.ToString(),
                    "ShowDisk=" + ShowDisk.ToString(),
                    "ShowNet=" + ShowNet.ToString(),
                    "ShowIpLocal=" + ShowIpLocal.ToString(),
                    "ShowIpPublic=" + ShowIpPublic.ToString()
                };
                string text = string.Join(Environment.NewLine, lines) + Environment.NewLine;

                // Não regrava quando nada mudou: o registo interno passa a
                // mostrar apenas as alterações reais feitas pelo utilizador.
                string current = null;
                try
                {
                    if (File.Exists(FilePath)) current = File.ReadAllText(FilePath);
                }
                catch
                {
                }
                if (string.Equals(current, text, StringComparison.Ordinal)) return;

                WriteAtomic(FilePath, text);
                Log.Write("Definições gravadas: " + Describe(this));
            }
            catch (Exception ex)
            {
                Log.Write("Falha ao gravar configurações: " + ex.Message);
            }
        }

        /// <summary>
        /// Lê as definições. Se o ficheiro principal não existir ou estiver
        /// incompleto, recorre à cópia deixada pela escrita atómica, para que
        /// o widget não volte a arrancar com todas as opções por engano.
        /// </summary>
        private static string ReadSettingsText()
        {
            string path = FilePath;
            try
            {
                if (File.Exists(path) && LooksLikeSettings(path)) return File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                Log.Write("Falha ao ler configurações: " + ex.Message);
            }

            string backup = path + ".bak";
            try
            {
                if (File.Exists(backup) && LooksLikeSettings(backup))
                {
                    Log.Write("Definições recuperadas de settings.ini.bak.");
                    return File.ReadAllText(backup);
                }
            }
            catch
            {
            }
            return null;
        }

        /// <summary>
        /// Um ficheiro de definições válido contém a primeira chave (IntervalMs).
        /// Um ficheiro truncado por uma gravação interrompida não a contém.
        /// </summary>
        private static bool LooksLikeSettings(string path)
        {
            using (StreamReader reader = new StreamReader(path))
            {
                string line;
                int seen = 0;
                while ((line = reader.ReadLine()) != null && seen < 32)
                {
                    string trimmed = line.Trim();
                    if (trimmed.Length == 0 || trimmed.StartsWith("#")) continue;
                    if (trimmed.StartsWith("IntervalMs=", StringComparison.OrdinalIgnoreCase)) return true;
                    seen++;
                }
            }
            return false;
        }

        /// <summary>
        /// Escreve em duas fases: primeiro um temporário completo, depois
        /// substituição atómica (deixando a versão anterior em .bak). Um
        /// processo terminado a meio da gravação — o que acontece quando o
        /// instalador encerra a app durante uma atualização, ou quando o
        /// Windows encerra a sessão — nunca deixa o ficheiro principal truncado.
        /// </summary>
        private static void WriteAtomic(string path, string text)
        {
            string temp = path + ".tmp";
            File.WriteAllText(temp, text);
            if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
            else File.Move(temp, path);
        }

        /// <summary>Resumo do estado, usado no registo interno da app.</summary>
        private static string Describe(Settings s)
        {
            System.Collections.Generic.List<string> enabled =
                new System.Collections.Generic.List<string>();
            foreach (BarItemKind kind in WidgetItems.Order)
            {
                if (WidgetItems.IsEnabled(s, kind)) enabled.Add(WidgetItems.MenuLabel(kind));
            }
            return "itens=[" + string.Join(", ", enabled.ToArray()) + "] visível=" + s.Visible;
        }

        private static bool ReadBool(string value, bool fallback)
        {
            bool v;
            if (!bool.TryParse(value, out v)) return fallback;
            return v;
        }
    }

    internal static class Log
    {
        private static readonly object Gate = new object();

        public static void Write(string message)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Settings.DataDirectory);
                    string path = Path.Combine(Settings.DataDirectory, "TaskbarTemps.log");
                    FileInfo fi = new FileInfo(path);
                    if (fi.Exists && fi.Length > 512 * 1024)
                    {
                        File.Delete(path);
                    }
                    File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)
                        + "  " + message + Environment.NewLine);
                }
            }
            catch
            {
                // registar nunca deve derrubar a aplicação
            }
        }
    }

    internal sealed class Palette
    {
        public bool Light;
        public Color Background;
        public Color Border;
        public Color Label;
        public Color Value;
        public Color AccentCpu;
        public Color AccentRam;
        public Color AccentGpu;
        public Color AccentDisk;
        public Color AccentNet;
        public Color AccentIp;
        public Color AccentIpPublic;
        public Color Warn;
        public Color Hot;

        public static Palette For(bool light)
        {
            Palette p = new Palette();
            p.Light = light;
            if (light)
            {
                p.Background = Color.FromArgb(228, 252, 252, 252);
                p.Border = Color.FromArgb(42, 20, 30, 50);
                p.Label = Color.FromArgb(255, 96, 100, 110);
                p.Value = Color.FromArgb(255, 28, 32, 40);
                p.AccentCpu = Color.FromArgb(255, 37, 99, 235);
                p.AccentRam = Color.FromArgb(255, 124, 58, 237);
                p.AccentGpu = Color.FromArgb(255, 5, 150, 105);
                p.AccentDisk = Color.FromArgb(255, 217, 119, 6);
                p.AccentNet = Color.FromArgb(255, 8, 145, 178);
                p.AccentIp = Color.FromArgb(255, 99, 102, 241);
                p.AccentIpPublic = Color.FromArgb(255, 219, 39, 119);
                p.Warn = Color.FromArgb(255, 180, 83, 9);
                p.Hot = Color.FromArgb(255, 220, 38, 38);
            }
            else
            {
                p.Background = Color.FromArgb(222, 30, 32, 38);
                p.Border = Color.FromArgb(46, 255, 255, 255);
                p.Label = Color.FromArgb(255, 168, 174, 186);
                p.Value = Color.FromArgb(255, 245, 247, 250);
                p.AccentCpu = Color.FromArgb(255, 96, 165, 250);
                p.AccentRam = Color.FromArgb(255, 196, 181, 253);
                p.AccentGpu = Color.FromArgb(255, 110, 231, 183);
                p.AccentDisk = Color.FromArgb(255, 251, 146, 60);
                p.AccentNet = Color.FromArgb(255, 103, 232, 249);
                p.AccentIp = Color.FromArgb(255, 165, 180, 252);
                p.AccentIpPublic = Color.FromArgb(255, 244, 114, 182);
                p.Warn = Color.FromArgb(255, 251, 191, 36);
                p.Hot = Color.FromArgb(255, 248, 113, 113);
            }
            return p;
        }

        public bool Equals(Palette other)
        {
            return other != null
                && other.Light == Light
                && other.Background == Background
                && other.Border == Border
                && other.Label == Label
                && other.Value == Value
                && other.AccentCpu == AccentCpu
                && other.AccentDisk == AccentDisk
                && other.AccentIp == AccentIp
                && other.AccentIpPublic == AccentIpPublic;
        }
    }

    internal static class ThemeManager
    {
        public static bool IsLightTaskbar()
        {
            try
            {
                object v = Microsoft.Win32.Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "SystemUsesLightTheme", 1);
                if (v == null) return true;
                return Convert.ToInt32(v, CultureInfo.InvariantCulture) != 0;
            }
            catch
            {
                return true;
            }
        }

        public static Palette Create(string mode)
        {
            if (mode == "light") return Palette.For(true);
            if (mode == "dark") return Palette.For(false);
            return Palette.For(IsLightTaskbar());
        }
    }
}
