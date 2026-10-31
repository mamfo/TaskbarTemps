using System;

namespace TaskbarTemps
{
    internal enum BarItemKind
    {
        Cpu,
        Ram,
        Gpu,
        Disk,
        Net,
        IpLocal,
        IpPublic
    }

    /// <summary>
    /// Descreve os itens que podem aparecer no widget e lê/grava a respetiva
    /// preferência nas definições.
    /// </summary>
    internal static class WidgetItems
    {
        public static readonly BarItemKind[] Order = new BarItemKind[]
        {
            BarItemKind.Cpu,
            BarItemKind.Ram,
            BarItemKind.Gpu,
            BarItemKind.Disk,
            BarItemKind.Net,
            BarItemKind.IpLocal,
            BarItemKind.IpPublic
        };

        /// <summary>Texto curto desenhado na barra de tarefas.</summary>
        public static string Label(BarItemKind kind)
        {
            switch (kind)
            {
                case BarItemKind.Cpu: return "CPU";
                case BarItemKind.Ram: return "RAM";
                case BarItemKind.Gpu: return "GPU";
                case BarItemKind.Disk: return "DISCO";
                case BarItemKind.Net: return "REDE";
                case BarItemKind.IpLocal: return "IP LAN";
                default: return "IP WAN";
            }
        }

        /// <summary>Texto usado no menu de contexto.</summary>
        public static string MenuLabel(BarItemKind kind)
        {
            switch (kind)
            {
                case BarItemKind.Cpu: return "CPU";
                case BarItemKind.Ram: return "RAM";
                case BarItemKind.Gpu: return "GPU";
                case BarItemKind.Disk: return "Disco";
                case BarItemKind.Net: return "Rede";
                case BarItemKind.IpLocal: return "IP interno (LAN)";
                default: return "IP público (WAN)";
            }
        }

        /// <summary>Valor de referência que reserva a largura de cada item.</summary>
        public static string Slot(BarItemKind kind)
        {
            switch (kind)
            {
                case BarItemKind.Cpu:
                case BarItemKind.Ram:
                case BarItemKind.Gpu:
                    return "99° 99%";
                case BarItemKind.Disk:
                    return "L999M E999M";
                case BarItemKind.Net:
                    return "▼999M ▲999M";
                default:
                    return "255.255.255.255";
            }
        }

        public static bool IsEnabled(Settings settings, BarItemKind kind)
        {
            switch (kind)
            {
                case BarItemKind.Cpu: return settings.ShowCpu;
                case BarItemKind.Ram: return settings.ShowRam;
                case BarItemKind.Gpu: return settings.ShowGpu;
                case BarItemKind.Disk: return settings.ShowDisk;
                case BarItemKind.Net: return settings.ShowNet;
                case BarItemKind.IpLocal: return settings.ShowIpLocal;
                default: return settings.ShowIpPublic;
            }
        }

        public static void SetEnabled(Settings settings, BarItemKind kind, bool value)
        {
            switch (kind)
            {
                case BarItemKind.Cpu: settings.ShowCpu = value; break;
                case BarItemKind.Ram: settings.ShowRam = value; break;
                case BarItemKind.Gpu: settings.ShowGpu = value; break;
                case BarItemKind.Disk: settings.ShowDisk = value; break;
                case BarItemKind.Net: settings.ShowNet = value; break;
                case BarItemKind.IpLocal: settings.ShowIpLocal = value; break;
                default: settings.ShowIpPublic = value; break;
            }
        }

        public static int CountEnabled(Settings settings)
        {
            int count = 0;
            foreach (BarItemKind kind in Order)
            {
                if (IsEnabled(settings, kind)) count++;
            }
            return count;
        }
    }
}
