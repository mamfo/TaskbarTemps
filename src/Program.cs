using System;
using System.Threading;
using System.Windows.Forms;

namespace TaskbarTemps
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (args != null && args.Length > 0)
            {
                string error;
                if (args[0] == "--install-startup")
                {
                    Environment.ExitCode = StartupManager.SetEnabled(true, out error) ? 0 : 1;
                    return;
                }
                if (args[0] == "--remove-startup")
                {
                    Environment.ExitCode = StartupManager.SetEnabled(false, out error) ? 0 : 1;
                    return;
                }
                if (args[0] == "--uninstall")
                {
                    Environment.ExitCode = Uninstaller.Run() ? 0 : 1;
                    return;
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += delegate (object sender, ThreadExceptionEventArgs e)
            {
                Log.Write("Exceção na interface: " + e.Exception);
            };

            bool createdNew;
            using (Mutex mutex = new Mutex(true, "Local\\TaskbarTemps.SingleInstance", out createdNew))
            {
                if (!createdNew) return;

                try
                {
                    using (TrayAppContext context = new TrayAppContext())
                    {
                        Application.Run(context);
                    }
                }
                catch (Exception ex)
                {
                    Log.Write("Erro fatal: " + ex);
                    MessageBox.Show(
                        "O TaskbarTemps não conseguiu iniciar.\n\n" + ex.Message,
                        "TaskbarTemps",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }

                GC.KeepAlive(mutex);
            }
        }
    }
}
