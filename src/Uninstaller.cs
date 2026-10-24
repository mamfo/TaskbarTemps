using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TaskbarTemps
{
    /// <summary>
    /// Remove a instalação criada pelo TaskbarTempsSetup.
    /// Executado pelo atalho de "Aplicações instaladas" com o argumento --uninstall.
    /// </summary>
    internal static class Uninstaller
    {
        public const string UninstallKeyPath =
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\TaskbarTemps";

        public static string ShortcutPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    @"Microsoft\Windows\Start Menu\Programs\TaskbarTemps.lnk");
            }
        }

        public static bool Run()
        {
            string directory = Path.GetDirectoryName(Application.ExecutablePath);
            Log.Write("Desinstalação iniciada em " + directory);

            string error;
            if (!StartupManager.SetEnabled(false, out error) && !string.IsNullOrEmpty(error))
            {
                Log.Write("Desinstalação: falha ao remover a tarefa agendada: " + error);
            }

            try
            {
                if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath);
            }
            catch (Exception ex)
            {
                Log.Write("Desinstalação: falha ao remover o atalho: " + ex.Message);
            }

            try
            {
                Registry.LocalMachine.DeleteSubKeyTree(UninstallKeyPath, false);
            }
            catch (Exception ex)
            {
                Log.Write("Desinstalação: falha ao remover a entrada de desinstalação: " + ex.Message);
            }

            KillOtherInstances();

            try
            {
                ProcessStartInfo info = new ProcessStartInfo(
                    "cmd.exe",
                    "/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"" + directory + "\"");
                info.CreateNoWindow = true;
                info.UseShellExecute = false;
                info.WindowStyle = ProcessWindowStyle.Hidden;
                Process.Start(info);
            }
            catch (Exception ex)
            {
                Log.Write("Desinstalação: falha ao agendar a remoção da pasta: " + ex.Message);
                return false;
            }

            return true;
        }

        private static void KillOtherInstances()
        {
            int current = Process.GetCurrentProcess().Id;
            foreach (Process process in Process.GetProcessesByName("TaskbarTemps"))
            {
                if (process.Id == current) continue;
                try
                {
                    process.Kill();
                    process.WaitForExit(3000);
                }
                catch
                {
                }
            }
        }
    }
}
