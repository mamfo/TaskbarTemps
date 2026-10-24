using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;

namespace TaskbarTemps
{
    internal static class StartupManager
    {
        private const string TaskName = "TaskbarTemps";

        public static bool IsEnabled()
        {
            ProcessResult result = Run("/Query /TN \"" + TaskName + "\"");
            return result.ExitCode == 0;
        }

        public static bool SetEnabled(bool enabled, out string error)
        {
            error = string.Empty;

            if (enabled)
            {
                return CreateTask(out error);
            }

            ProcessResult result = Run("/Delete /TN \"" + TaskName + "\" /F");
            if (result.ExitCode == 0) return true;
            error = Describe(result);
            return false;
        }

        private static bool CreateTask(out string error)
        {
            error = string.Empty;
            string executable = Application.ExecutablePath;
            string directory = Path.GetDirectoryName(executable);
            string userId;

            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    userId = identity.User.Value;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }

            // O schtasks não lida bem com /TR quando o caminho tem espaços:
            // a tarefa é criada a partir de XML para preservar o caminho completo.
            string xml = "<?xml version=\"1.0\" encoding=\"UTF-16\"?>" + Environment.NewLine
                + "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">" + Environment.NewLine
                + "  <RegistrationInfo>" + Environment.NewLine
                + "    <Description>TaskbarTemps - temperaturas na barra de tarefas</Description>" + Environment.NewLine
                + "  </RegistrationInfo>" + Environment.NewLine
                + "  <Triggers>" + Environment.NewLine
                + "    <LogonTrigger>" + Environment.NewLine
                + "      <Enabled>true</Enabled>" + Environment.NewLine
                + "    </LogonTrigger>" + Environment.NewLine
                + "  </Triggers>" + Environment.NewLine
                + "  <Principals>" + Environment.NewLine
                + "    <Principal id=\"Author\">" + Environment.NewLine
                + "      <UserId>" + Xml(userId) + "</UserId>" + Environment.NewLine
                + "      <LogonType>InteractiveToken</LogonType>" + Environment.NewLine
                + "      <RunLevel>HighestAvailable</RunLevel>" + Environment.NewLine
                + "    </Principal>" + Environment.NewLine
                + "  </Principals>" + Environment.NewLine
                + "  <Settings>" + Environment.NewLine
                + "    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>" + Environment.NewLine
                + "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>" + Environment.NewLine
                + "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>" + Environment.NewLine
                + "    <StartWhenAvailable>false</StartWhenAvailable>" + Environment.NewLine
                + "    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>" + Environment.NewLine
                + "    <AllowStartOnDemand>true</AllowStartOnDemand>" + Environment.NewLine
                + "    <Enabled>true</Enabled>" + Environment.NewLine
                + "    <Hidden>false</Hidden>" + Environment.NewLine
                + "    <RunOnlyIfIdle>false</RunOnlyIfIdle>" + Environment.NewLine
                + "    <WakeToRun>false</WakeToRun>" + Environment.NewLine
                + "    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>" + Environment.NewLine
                + "    <Priority>7</Priority>" + Environment.NewLine
                + "  </Settings>" + Environment.NewLine
                + "  <Actions Context=\"Author\">" + Environment.NewLine
                + "    <Exec>" + Environment.NewLine
                + "      <Command>" + Xml(executable) + "</Command>" + Environment.NewLine
                + "      <WorkingDirectory>" + Xml(directory) + "</WorkingDirectory>" + Environment.NewLine
                + "    </Exec>" + Environment.NewLine
                + "  </Actions>" + Environment.NewLine
                + "</Task>";

            string file = Path.Combine(Path.GetTempPath(),
                "TaskbarTemps-" + Guid.NewGuid().ToString("N") + ".task.xml");
            ProcessResult result = new ProcessResult();
            result.ExitCode = -1;
            bool created = false;
            try
            {
                using (FileStream stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                {
                    created = true;
                    using (StreamWriter writer = new StreamWriter(stream, Encoding.Unicode, 4096, true))
                    {
                        writer.Write(xml);
                    }
                    stream.Flush(true);
                    result = Run("/Create /TN \"" + TaskName + "\" /XML \"" + file + "\" /F");
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                if (created)
                {
                    try { File.Delete(file); } catch { }
                }
            }

            if (result.ExitCode == 0) return true;
            error = Describe(result);
            return false;
        }

        private static string Xml(string value)
        {
            return value
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;");
        }

        private static string Describe(ProcessResult result)
        {
            string detail = FirstLine(result.Error);
            if (detail.Length == 0) detail = FirstLine(result.Output);
            if (detail.Length == 0)
            {
                detail = "O agendador de tarefas devolveu o código " + result.ExitCode + ".";
            }
            return detail;
        }

        private static string FirstLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            foreach (string line in text.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = line.Trim();
                if (trimmed.Length > 0) return trimmed;
            }
            return string.Empty;
        }

        private struct ProcessResult
        {
            public int ExitCode;
            public string Output;
            public string Error;
        }

        private static ProcessResult Run(string arguments)
        {
            ProcessResult result = new ProcessResult();
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "schtasks.exe";
                psi.Arguments = arguments;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.StandardOutputEncoding = Encoding.Default;
                psi.StandardErrorEncoding = Encoding.Default;

                using (Process process = Process.Start(psi))
                {
                    result.Output = process.StandardOutput.ReadToEnd();
                    result.Error = process.StandardError.ReadToEnd();
                    process.WaitForExit(8000);
                    if (!process.HasExited)
                    {
                        try { process.Kill(); } catch { }
                        result.ExitCode = -1;
                        result.Error = "O schtasks.exe não respondeu a tempo.";
                        return result;
                    }
                    result.ExitCode = process.ExitCode;
                }
            }
            catch (Exception ex)
            {
                result.ExitCode = -1;
                result.Error = ex.Message;
            }
            return result;
        }
    }
}
