using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
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
        public string Sha256Url = string.Empty;
        public string Sha256 = string.Empty;
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

                    if (info.SetupUrl.Length == 0 && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        info.SetupUrl = Text(asset, "browser_download_url");
                        object size;
                        if (asset.TryGetValue("size", out size) && size != null)
                        {
                            try { info.SetupSize = Convert.ToInt64(size, CultureInfo.InvariantCulture); } catch { }
                        }

                        string digest = Text(asset, "digest");
                        if (digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                        {
                            string value = digest.Substring(7).Trim().ToLowerInvariant();
                            if (IsSha256(value)) info.Sha256 = value;
                        }
                    }
                    else if (info.Sha256Url.Length == 0 && name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase))
                    {
                        info.Sha256Url = Text(asset, "browser_download_url");
                    }
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

        /// <summary>Descarrega e verifica o instalador da release para %TEMP%.</summary>
        public static string Download(UpdateInfo info, Action<int> progress)
        {
            if (info == null) throw new InvalidOperationException("Não há atualização para descarregar.");
            if (string.IsNullOrEmpty(info.SetupUrl))
            {
                throw new InvalidOperationException(
                    "A release não tem o instalador TaskbarTempsSetup.exe anexado.");
            }
            if (string.IsNullOrEmpty(info.Sha256) && string.IsNullOrEmpty(info.Sha256Url))
            {
                throw new InvalidOperationException(
                    "A release não publica a verificação SHA-256 do instalador. "
                    + "Instale manualmente a partir de " + ReleasePage + ".");
            }

            string expected = string.IsNullOrEmpty(info.Sha256)
                ? FetchExpectedHash(info)
                : info.Sha256;
            string version = info.Version == null ? "nova" : info.Version.ToString(3);
            string path = Path.Combine(Path.GetTempPath(),
                "TaskbarTempsSetup-" + version + "-" + Guid.NewGuid().ToString("N") + ".exe");
            bool created = false;

            try
            {
                using (WebClient client = new WebClient())
                {
                    client.Headers[HttpRequestHeader.UserAgent] = UserAgent;
                    using (Stream input = client.OpenRead(info.SetupUrl))
                    using (FileStream output = new FileStream(path, FileMode.CreateNew,
                        FileAccess.Write, FileShare.None))
                    {
                        created = true;
                        CopyWithProgress(input, output, ContentLength(client), progress);
                        output.Flush(true);
                    }
                }

                long length = new FileInfo(path).Length;
                if (info.SetupSize > 0 && length != info.SetupSize)
                {
                    throw new InvalidOperationException(
                        "O instalador descarregado tem um tamanho diferente do anunciado pela release.");
                }

                string actual = ComputeSha256(path);
                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "A verificação SHA-256 do instalador falhou. O ficheiro foi eliminado.");
                }

                info.Sha256 = expected;
                return path;
            }
            catch
            {
                if (created) TryDelete(path);
                throw;
            }
        }

        private static string FetchExpectedHash(UpdateInfo info)
        {
            string text;
            using (WebClient client = new WebClient())
            {
                client.Headers[HttpRequestHeader.UserAgent] = UserAgent;
                text = client.DownloadString(info.Sha256Url);
            }

            foreach (string raw in text.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                string token = raw.Trim().TrimStart('\uFEFF');
                if (token.Length == 0) continue;
                int space = token.IndexOfAny(new char[] { ' ', '\t' });
                if (space > 0) token = token.Substring(0, space);
                token = token.Trim().ToLowerInvariant();
                if (IsSha256(token)) return token;
            }

            throw new InvalidOperationException(
                "O ficheiro SHA-256 da release não tem um valor válido.");
        }

        private static bool IsSha256(string text)
        {
            if (text == null || text.Length != 64) return false;
            foreach (char c in text)
            {
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
                if (!hex) return false;
            }
            return true;
        }

        private static long ContentLength(WebClient client)
        {
            string value = client.ResponseHeaders == null ? null : client.ResponseHeaders["Content-Length"];
            long length;
            if (!string.IsNullOrEmpty(value)
                && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out length))
            {
                return length;
            }
            return -1;
        }

        private static void CopyWithProgress(Stream input, Stream output, long total, Action<int> progress)
        {
            byte[] buffer = new byte[81920];
            long received = 0;
            int last = -1;
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                output.Write(buffer, 0, read);
                received += read;
                if (progress != null && total > 0)
                {
                    int percent = (int)(received * 100L / total);
                    if (percent > 100) percent = 100;
                    if (percent != last)
                    {
                        last = percent;
                        progress(percent);
                    }
                }
            }
            if (progress != null && total > 0 && last < 100) progress(100);
        }

        public static string ComputeSha256(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(stream);
                StringBuilder text = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash) text.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                return text.ToString();
            }
        }

        public static bool VerifyFile(string path, string expected)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(expected) || !File.Exists(path)) return false;
            return string.Equals(ComputeSha256(path), expected, StringComparison.OrdinalIgnoreCase);
        }

        private static void TryDelete(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try { if (File.Exists(path)) File.Delete(path); } catch { }
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
