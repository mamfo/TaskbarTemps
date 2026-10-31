using System;
using System.Globalization;
using System.Text;

namespace TaskbarTemps
{
    internal static class DisplayFormat
    {
        private static readonly CultureInfo Ci = CultureInfo.CurrentCulture;

        public static string Round(float value)
        {
            return Math.Round(value, MidpointRounding.AwayFromZero).ToString("0", Ci);
        }

        public static string Temp(float? value)
        {
            return value.HasValue ? Round(value.Value) + " °C" : "—";
        }

        public static string ShortTemp(float? value)
        {
            return value.HasValue ? Round(value.Value) + "°" : "--";
        }

        public static string Load(float? value)
        {
            return value.HasValue ? Round(value.Value) + " %" : "—";
        }

        public static string ShortLoad(float? value)
        {
            return value.HasValue ? Round(value.Value) + "%" : "--";
        }

        public static string ShortValue(float? temperature, float? load, bool usageWhenNoTemp)
        {
            if (temperature.HasValue) return ShortTemp(temperature);
            if (usageWhenNoTemp && load.HasValue) return ShortLoad(load);
            return "--";
        }

        public static string Rate(double? bytesPerSecond)
        {
            if (!bytesPerSecond.HasValue) return "—";
            double value = bytesPerSecond.Value;
            if (value < 0d) value = 0d;
            if (value < 1024d) return Round((float)value) + " B/s";
            if (value < 1024d * 1024d) return (value / 1024d).ToString("0.0", Ci) + " KB/s";
            if (value < 1024d * 1024d * 1024d) return (value / (1024d * 1024d)).ToString("0.0", Ci) + " MB/s";
            return (value / (1024d * 1024d * 1024d)).ToString("0.0", Ci) + " GB/s";
        }

        public static string ShortRate(double? bytesPerSecond)
        {
            if (!bytesPerSecond.HasValue) return "--";
            double value = bytesPerSecond.Value;
            if (value < 0d) value = 0d;
            if (value < 1024d) return Round((float)value);
            if (value < 1024d * 1024d) return Round((float)(value / 1024d)) + "K";
            if (value < 1024d * 1024d * 1024d) return (value / (1024d * 1024d)).ToString("0.0", Ci) + "M";
            return (value / (1024d * 1024d * 1024d)).ToString("0.0", Ci) + "G";
        }

        public static string TrayText(SensorSnapshot s, bool usageWhenNoTemp)
        {
            string cpu = "CPU " + ShortValue(s.CpuTemp, s.CpuLoad, usageWhenNoTemp);
            if (s.CpuTemp.HasValue && s.CpuLoad.HasValue) cpu += " " + ShortLoad(s.CpuLoad);

            string gpu = "GPU " + ShortValue(s.GpuTemp, s.GpuLoad, usageWhenNoTemp);
            if (s.GpuTemp.HasValue && s.GpuLoad.HasValue) gpu += " " + ShortLoad(s.GpuLoad);

            string ram = "RAM " + ShortValue(s.RamTemp, s.RamLoad, usageWhenNoTemp);
            if (s.RamTemp.HasValue && s.RamLoad.HasValue) ram += " " + ShortLoad(s.RamLoad);

            string text = cpu
                + "  ·  " + ram
                + "  ·  " + gpu;

            if (s.DiskAvailable)
            {
                text += "  ·  L" + ShortRate(s.DiskReadBps) + " E" + ShortRate(s.DiskWriteBps);
            }

            if (s.NetAvailable)
            {
                string net = "  ·  ↓" + ShortRate(s.NetDownBps) + " ↑" + ShortRate(s.NetUpBps);
                if (text.Length + net.Length <= 63) text += net;
            }
            return text.Length <= 63 ? text : text.Substring(0, 63);
        }

        public static string Tooltip(SensorSnapshot s, bool usageWhenNoTemp)
        {
            StringBuilder sb = new StringBuilder();

            sb.Append("CPU  ");
            if (s.CpuTemp.HasValue) sb.Append(Temp(s.CpuTemp));
            else if (!s.IsAdmin) sb.Append("— (execute como admin)");
            else sb.Append("— (é necessário o PawnIO)");
            if (s.CpuLoad.HasValue) sb.Append("  ·  uso " + Load(s.CpuLoad));
            if (s.CpuPower.HasValue) sb.Append("  ·  " + s.CpuPower.Value.ToString("0.0", Ci) + " W");
            sb.AppendLine();

            sb.Append("RAM  ");
            if (s.RamTemp.HasValue) sb.Append(Temp(s.RamTemp));
            else sb.Append("— (sem sensor)");
            if (s.RamLoad.HasValue) sb.Append("  ·  uso " + Load(s.RamLoad));
            if (s.RamTotalGb.HasValue) sb.Append("  ·  " + s.RamTotalGb.Value.ToString("0.0", Ci) + " GB");
            sb.AppendLine();

            sb.Append("GPU  ");
            sb.Append(Temp(s.GpuTemp));
            if (s.GpuHotspot.HasValue) sb.Append("  ·  hot spot " + Temp(s.GpuHotspot));
            if (s.GpuLoad.HasValue) sb.Append("  ·  uso " + Load(s.GpuLoad));
            if (s.GpuFan.HasValue) sb.Append("  ·  " + Round(s.GpuFan.Value) + " RPM");
            sb.AppendLine();

            sb.Append("Disco  ");
            if (s.DiskAvailable)
            {
                sb.Append("leitura " + Rate(s.DiskReadBps) + "  ·  escrita " + Rate(s.DiskWriteBps));
            }
            else
            {
                sb.Append("— (contadores indisponíveis)");
            }
            sb.AppendLine();

            sb.Append("Rede  ");
            if (s.NetAvailable)
            {
                sb.Append("↓ " + Rate(s.NetDownBps) + "  ·  ↑ " + Rate(s.NetUpBps));
                if (!string.IsNullOrEmpty(s.NetName) && s.NetName != "Rede") sb.Append("  ·  " + s.NetName);
            }
            else
            {
                sb.Append("— (sem ligação)");
            }
            sb.AppendLine();

            if (!string.IsNullOrEmpty(s.IpLocal))
            {
                sb.AppendLine("IP interno  " + s.IpLocal);
            }

            if (!string.IsNullOrEmpty(s.IpPublic))
            {
                sb.AppendLine("IP público  " + s.IpPublic);
            }
            else if (s.IpPublicEnabled)
            {
                sb.AppendLine("IP público  a obter…");
            }

            if (!string.IsNullOrEmpty(s.Error)) sb.AppendLine("Aviso: " + s.Error);
            sb.Append("Clique para ver todos os sensores");
            return sb.ToString();
        }

        public static string DetailSummaryCpu(SensorSnapshot s)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("CPU  ·  " + s.CpuName + Environment.NewLine);
            sb.Append("Temperatura: " + Temp(s.CpuTemp));
            if (!s.CpuTemp.HasValue && s.IsAdmin) sb.Append("  (é necessário o PawnIO)");
            if (!string.IsNullOrEmpty(s.CpuTempSource)) sb.Append("  (" + s.CpuTempSource + ")");
            if (s.CpuLoad.HasValue) sb.Append("   ·   uso " + Load(s.CpuLoad));
            if (s.CpuPower.HasValue) sb.Append("   ·   " + s.CpuPower.Value.ToString("0.0", Ci) + " W");
            return sb.ToString();
        }

        public static string DetailSummaryRam(SensorSnapshot s)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("RAM  ·  " + (s.RamTotalGb.HasValue
                ? s.RamTotalGb.Value.ToString("0.0", Ci) + " GB"
                : "memória") + Environment.NewLine);
            if (s.RamTemp.HasValue)
            {
                sb.Append("Temperatura: " + Temp(s.RamTemp));
                if (!string.IsNullOrEmpty(s.RamTempSource)) sb.Append("  (" + s.RamTempSource + ")");
            }
            else
            {
                sb.Append("Sem sensor de temperatura (a mostrar uso)");
            }
            if (s.RamLoad.HasValue) sb.Append("   ·   uso " + Load(s.RamLoad));
            return sb.ToString();
        }

        public static string DetailSummaryGpu(SensorSnapshot s)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("GPU  ·  " + s.GpuName + Environment.NewLine);
            sb.Append("Temperatura: " + Temp(s.GpuTemp));
            if (s.GpuHotspot.HasValue) sb.Append("   ·   hot spot " + Temp(s.GpuHotspot));
            if (s.GpuLoad.HasValue) sb.Append("   ·   uso " + Load(s.GpuLoad));
            if (s.GpuFan.HasValue) sb.Append("   ·   " + Round(s.GpuFan.Value) + " RPM");
            if (s.GpuMemUsedMb.HasValue && s.GpuMemTotalMb.HasValue)
            {
                sb.Append("   ·   VRAM " + Round(s.GpuMemUsedMb.Value) + "/" + Round(s.GpuMemTotalMb.Value) + " MB");
            }
            return sb.ToString();
        }

        public static string DetailSummaryDisk(SensorSnapshot s)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("Disco  ·  leitura e escrita" + Environment.NewLine);
            if (s.DiskAvailable)
            {
                sb.Append("Leitura: " + Rate(s.DiskReadBps));
                sb.Append("   ·   escrita: " + Rate(s.DiskWriteBps));
            }
            else
            {
                sb.Append("Contadores de disco indisponíveis");
            }
            return sb.ToString();
        }

        public static string DetailSummaryNetwork(SensorSnapshot s)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("Rede  ·  " + (string.IsNullOrEmpty(s.NetName) ? "—" : s.NetName) + Environment.NewLine);
            if (s.NetAvailable)
            {
                sb.Append("Receção: " + Rate(s.NetDownBps));
                sb.Append("   ·   envio: " + Rate(s.NetUpBps));
            }
            else
            {
                sb.Append("Sem ligação de rede ativa");
            }
            return sb.ToString();
        }

        public static string DetailSummaryIp(SensorSnapshot s)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("Endereços IP" + Environment.NewLine);
            sb.Append("Interno: " + (string.IsNullOrEmpty(s.IpLocal) ? "—" : s.IpLocal));

            if (!string.IsNullOrEmpty(s.IpPublic))
            {
                sb.Append("   ·   público: " + s.IpPublic);
            }
            else if (s.IpPublicEnabled)
            {
                sb.Append("   ·   público: a obter…");
            }
            else
            {
                sb.Append("   ·   público: — (ative em \"Itens no widget\")");
            }
            return sb.ToString();
        }
    }
}
