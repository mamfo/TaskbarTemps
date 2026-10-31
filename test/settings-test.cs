using System;
using System.IO;

namespace TaskbarTemps
{
    /// <summary>
    /// Verificador da persistência das definições. Não faz parte da aplicação:
    /// é compilado à parte por test\settings-test.ps1 para exercitar o
    /// Settings carregado a partir de um ficheiro isolado.
    /// </summary>
    internal static class SettingsTestMain
    {
        private static int Main(string[] args)
        {
            string report = args != null && args.Length > 1 ? args[1] : null;
            TextWriter console = Console.Out;
            if (!string.IsNullOrEmpty(report))
            {
                console = new StreamWriter(report, false);
            }

            try
            {
                if (args != null && args.Length > 0 && !string.IsNullOrEmpty(args[0]))
                {
                    Environment.SetEnvironmentVariable("TBT_SETTINGS_FILE", args[0]);
                }

                string path = Settings.FilePath;
                console.WriteLine("ficheiro: " + path);
                console.WriteLine("existe antes: " + File.Exists(path));

                Settings s = Settings.Load();
                console.WriteLine("1) carregado: itens=" + Enabled(s) + " offset=" + s.OffsetX);

                // Gravação atómica: deve criar o .tmp (apagado no fim) e o .bak.
                s.OffsetX = 77;
                s.Save();
                console.WriteLine("2) após Save: offset=" + s.OffsetX
                    + " tmp=" + File.Exists(path + ".tmp")
                    + " bak=" + File.Exists(path + ".bak"));

                Settings s2 = Settings.Load();
                console.WriteLine("3) reload: itens=" + Enabled(s2) + " offset=" + s2.OffsetX
                    + " (esperado 77)");

                // Falha 1: o ficheiro principal desaparece.
                File.Delete(path);
                Settings s3 = Settings.Load();
                console.WriteLine("4) sem principal: itens=" + Enabled(s3) + " offset=" + s3.OffsetX
                    + " (esperado recuperar do .bak)");

                // Falha 2: o ficheiro principal fica truncado (era isto que
                // acontecia quando o processo era morto a meio da gravação).
                File.WriteAllText(path, "# TaskbarTemps - configuração truncada" + Environment.NewLine);
                Settings s4 = Settings.Load();
                console.WriteLine("5) principal truncado: itens=" + Enabled(s4) + " offset=" + s4.OffsetX
                    + " (esperado recuperar do .bak)");

                // O Save a partir de estado recuperado deve repor um principal válido.
                s4.Save();
                console.WriteLine("6) Save após recuperação: itens=" + Enabled(s4));
                console.WriteLine("7) ficheiro final: " + (File.Exists(path) ? "existe" : "FALTA"));

                console.WriteLine("FIM");
            }
            catch (Exception ex)
            {
                console.WriteLine("ERRO: " + ex);
                return 1;
            }
            finally
            {
                console.Flush();
                if (!object.ReferenceEquals(console, Console.Out)) console.Close();
            }
            return 0;
        }

        private static string Enabled(Settings s)
        {
            System.Collections.Generic.List<string> names =
                new System.Collections.Generic.List<string>();
            foreach (BarItemKind kind in WidgetItems.Order)
            {
                if (WidgetItems.IsEnabled(s, kind)) names.Add(WidgetItems.MenuLabel(kind));
            }
            return names.Count == 0 ? "(nenhum)" : string.Join(",", names.ToArray());
        }
    }
}
