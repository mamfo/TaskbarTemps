using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace TaskbarTemps
{
    internal sealed class UpdateInfo
    {
        public Version Version;
        public string Tag = string.Empty;
        public string ReleaseUrl = string.Empty;
        public string SetupUrl = string.Empty;
        public long SetupSize;
        public string Notes = string.Empty;
    }

    /// <summary>
    /// Verifica e descarrega atualizações publicadas no GitHub Releases.
    /// https://github.com/mamfo/TaskbarTemps/releases
    /// </summary>
    internal static class Updater
    {
        public const string Owner = "mamfo";
        public const string Repository = "TaskbarTemps";

        private const string ApiUrl =
            "https://api.github.com/repos/" + Owner + "/" + Repository + "/releases/latest";
        private const string UserAgent = "TaskbarTemps-Updater";

        static Updater()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            catch
            {
            }
        }

        public static Version InstalledVersion
        {
            get { return Assembly.GetExecutingAssembly().GetName().Version; }
        }

        public static string InstalledText
        {
            get { return InstalledVersion.ToString(3); }
        }

        public static string ReleasePage
        {
            get { return "https://github.com/" + Owner + "/" + Repository + "/releases"; }
        }

        public static Version VersionOf(UpdateInfo info)
        {
            return info == null || info.Version == null ? new Version(0, 0, 0) : info.Version;
        }

        public static bool IsNewer(UpdateInfo info)
        {
            return info != null && info.Version != null && info.Version > InstalledVersion;
        }

        public static void CheckAsync(Action<UpdateInfo> onSuccess, Action<Exception> onError)
        {
            Thread thread = new Thread(delegate ()
            {
                try
                {
                    UpdateInfo info = Check();
                    if (onSuccess != null) onSuccess(info);
                }
                catch (Exception ex)
                {
                    if (onError != null) onError(ex);
                }
            });
            thread.IsBackground = true;
            thread.Name = "TaskbarTemps.Atualizacoes";
            thread.Start();
        }

        /// <summary>Consulta a última release. Devolve null se não houver nenhuma.</summary>
        public static UpdateInfo Check()
        {
            string json;
            using (WebClient client = new WebClient())
            {
                client.Encoding = Encoding.UTF8;
                client.Headers[HttpRequestHeader.UserAgent] = UserAgent;
                client.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
                json = client.DownloadString(ApiUrl);
            }

            JavaScriptSerializer serializer = new JavaScriptSerializer();
            Dictionary<string, object> root = serializer.DeserializeObject(json) as Dictionary<string, object>;
            if (root == null) return null;

            UpdateInfo info = new UpdateInfo();
            info.Tag = Text(root, "tag_name");
            info.ReleaseUrl = Text(root, "html_url");
            info.Notes = Text(root, "body");
            info.Version = ParseVersion(info.Tag);
            if (info.Version == null) return null;

            object[] assets = root.ContainsKey("assets") ? root["assets"] as object[] : null;
            if (assets != null)
            {
                foreach (object item in assets)
                {
                    Dictionary<string, object> asset = item as Dictionary<string, object>;
                    if (asset == null) continue;
                    string name = Text(asset, "name");
                    if (name.IndexOf("TaskbarTempsSetup", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                    info.SetupUrl = Text(asset, "browser_download_url");
                    object size;
                    if (asset.TryGetValue("size", out size) && size != null)
                    {
                        try { info.SetupSize = Convert.ToInt64(size, CultureInfo.InvariantCulture); } catch { }
                    }
                    break;
                }
            }

            return info;
        }

        public static void DownloadAsync(UpdateInfo info, Action<int> progress,
            Action<string> onSuccess, Action<Exception> onError)
        {
            Thread thread = new Thread(delegate ()
            {
                try
                {
                    string path = Download(info, progress);
                    if (onSuccess != null) onSuccess(path);
                }
                catch (Exception ex)
                {
                    if (onError != null) onError(ex);
                }
            });
            thread.IsBackground = true;
            thread.Name = "TaskbarTemps.Download";
            thread.Start();
        }

        /// <summary>Descarrega o instalador da release para %TEMP% e devolve o caminho.</summary>
        public static string Download(UpdateInfo info, Action<int> progress)
        {
            if (info == null) throw new InvalidOperationException("Não há atualização para descarregar.");
            if (string.IsNullOrEmpty(info.SetupUrl))
            {
                throw new InvalidOperationException(
                    "A release não tem o instalador TaskbarTempsSetup.exe anexado.");
            }

            string version = info.Version == null ? "nova" : info.Version.ToString(3);
            string path = Path.Combine(Path.GetTempPath(), "TaskbarTempsSetup-" + version + ".exe");
            if (File.Exists(path))
            {
                try { File.Delete(path); } catch { }
            }

            using (WebClient client = new WebClient())
            {
                client.Headers[HttpRequestHeader.UserAgent] = UserAgent;
                if (progress != null)
                {
                    client.DownloadProgressChanged += delegate (object sender, DownloadProgressChangedEventArgs e)
                    {
                        progress(e.ProgressPercentage);
                    };
                }
                client.DownloadFile(info.SetupUrl, path);
            }

            return path;
        }

        private static string Text(Dictionary<string, object> map, string key)
        {
            object value;
            if (map == null || !map.TryGetValue(key, out value) || value == null) return string.Empty;
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static Version ParseVersion(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return null;
            string text = tag.Trim();
            if (text.StartsWith("v", StringComparison.OrdinalIgnoreCase)) text = text.Substring(1);

            StringBuilder digits = new StringBuilder();
            foreach (char c in text)
            {
                if ((c >= '0' && c <= '9') || c == '.') digits.Append(c);
                else break;
            }

            string clean = digits.ToString().Trim('.');
            if (clean.Length == 0) return null;

            // completa para Major.Minor.Build, como o AssemblyVersion
            string[] parts = clean.Split('.');
            int major = ParseInt(parts, 0);
            int minor = ParseInt(parts, 1);
            int build = ParseInt(parts, 2);
            int revision = ParseInt(parts, 3);
            if (major <= 0) return null;
            return new Version(major, minor, build, revision);
        }

        private static int ParseInt(string[] parts, int index)
        {
            if (index >= parts.Length) return 0;
            int value;
            if (!int.TryParse(parts[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) return 0;
            return value < 0 ? 0 : value;
        }
    }
}
