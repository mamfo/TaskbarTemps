using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.NetworkInformation;

namespace TaskbarTemps
{
    /// <summary>
    /// Mede o débito de receção e envio das placas de rede ativas.
    /// Usa os contadores de desempenho do Windows (que incluem IPv4 e IPv6) e,
    /// se estes não estiverem disponíveis, os contadores da pilha IPv4.
    /// </summary>
    internal sealed class NetworkUsage : IDisposable
    {
        private sealed class CounterPair
        {
            public PerformanceCounter Down;
            public PerformanceCounter Up;
            public string Name;
        }

        private static readonly string[] IgnoredHints = new string[]
        {
            "loopback", "isatap", "teredo", "pseudo", "vethernet", "virtual",
            "vmware", "virtualbox", "hyper-v", "tap-", "tun", "vpn", "wsl",
            "bluetooth", "npcap", "wireguard", "zerotier"
        };

        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private List<CounterPair> _counters;
        private bool _countersTried;

        private long _lastReceived = -1;
        private long _lastSent = -1;
        private long _lastTicks;

        public bool Available;
        public double DownloadBps;
        public double UploadBps;
        public string Name = "Rede";

        public void Sample()
        {
            if (!_countersTried)
            {
                _countersTried = true;
                TryOpenCounters();
            }

            if (_counters != null && _counters.Count > 0)
            {
                SampleCounters();
                return;
            }

            SampleInterfaceCounters();
        }

        private void TryOpenCounters()
        {
            try
            {
                PerformanceCounterCategory category = new PerformanceCounterCategory("Network Interface");
                string[] instances = category.GetInstanceNames();
                List<CounterPair> usable = new List<CounterPair>();
                List<CounterPair> all = new List<CounterPair>();

                foreach (string instance in instances)
                {
                    if (string.IsNullOrEmpty(instance)) continue;
                    CounterPair pair;
                    try
                    {
                        pair = new CounterPair();
                        pair.Name = instance;
                        pair.Down = new PerformanceCounter("Network Interface", "Bytes Received/sec", instance, true);
                        pair.Up = new PerformanceCounter("Network Interface", "Bytes Sent/sec", instance, true);
                        pair.Down.NextValue();
                        pair.Up.NextValue();
                    }
                    catch
                    {
                        continue;
                    }
                    all.Add(pair);
                    if (!IsIgnored(instance)) usable.Add(pair);
                }

                if (usable.Count == 0) usable = all;
                if (usable.Count > 0) _counters = usable;
            }
            catch
            {
                _counters = null;
            }
        }

        private void SampleCounters()
        {
            double down = 0d;
            double up = 0d;
            int active = 0;
            string name = null;

            foreach (CounterPair pair in _counters)
            {
                try
                {
                    down += pair.Down.NextValue();
                    up += pair.Up.NextValue();
                    active++;
                    if (name == null) name = pair.Name;
                    else if (name != pair.Name) name = string.Empty;
                }
                catch
                {
                }
            }

            Available = active > 0;
            Name = active == 1 && !string.IsNullOrEmpty(name) ? SafeName(name) : "Rede";
            DownloadBps = down < 0d ? 0d : down;
            UploadBps = up < 0d ? 0d : up;
        }

        private static string SafeName(string name)
        {
            return string.IsNullOrEmpty(name) || name.Length > 28 ? "Rede" : name;
        }

        private void SampleInterfaceCounters()
        {
            long received = 0;
            long sent = 0;
            int active = 0;
            string single = null;

            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (!IsUsable(ni)) continue;

                    long ifReceived;
                    long ifSent;
                    if (!TryRead(ni, out ifReceived, out ifSent)) continue;

                    received += ifReceived;
                    sent += ifSent;
                    active++;
                    if (single == null) single = ni.Name;
                    else if (single != ni.Name) single = string.Empty;
                }
            }
            catch
            {
                // sem acesso às estatísticas: mostra indisponível
            }

            Available = active > 0;
            Name = active == 1 && !string.IsNullOrEmpty(single) ? SafeName(single) : "Rede";

            long now = _clock.ElapsedTicks;
            if (_lastReceived >= 0 && now > _lastTicks)
            {
                double seconds = (now - _lastTicks) / (double)Stopwatch.Frequency;
                long deltaReceived = received - _lastReceived;
                long deltaSent = sent - _lastSent;
                if (seconds > 0.05 && deltaReceived >= 0 && deltaSent >= 0)
                {
                    DownloadBps = deltaReceived / seconds;
                    UploadBps = deltaSent / seconds;
                }
                else
                {
                    DownloadBps = 0d;
                    UploadBps = 0d;
                }
            }

            _lastReceived = received;
            _lastSent = sent;
            _lastTicks = now;
        }

        private static bool IsIgnored(string name)
        {
            string text = name.ToLowerInvariant();
            foreach (string hint in IgnoredHints)
            {
                if (text.IndexOf(hint, StringComparison.Ordinal) >= 0) return true;
            }
            return false;
        }

        private static bool IsUsable(NetworkInterface ni)
        {
            if (ni.OperationalStatus != OperationalStatus.Up) return false;
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) return false;
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) return false;

            switch (ni.NetworkInterfaceType)
            {
                case NetworkInterfaceType.Ethernet:
                case NetworkInterfaceType.GigabitEthernet:
                case NetworkInterfaceType.FastEthernetT:
                case NetworkInterfaceType.FastEthernetFx:
                case NetworkInterfaceType.Wireless80211:
                case NetworkInterfaceType.Ppp:
                    break;
                default:
                    return false;
            }

            return !IsIgnored(ni.Name + " " + ni.Description);
        }

        private static bool TryRead(NetworkInterface ni, out long received, out long sent)
        {
            received = 0;
            sent = 0;
            try
            {
                IPv4InterfaceStatistics stats = ni.GetIPv4Statistics();
                received = stats.BytesReceived;
                sent = stats.BytesSent;
            }
            catch
            {
            }
            return received > 0 || sent > 0;
        }

        public void Dispose()
        {
            if (_counters == null) return;
            foreach (CounterPair pair in _counters)
            {
                try
                {
                    if (pair.Down != null) pair.Down.Dispose();
                    if (pair.Up != null) pair.Up.Dispose();
                }
                catch
                {
                }
            }
            _counters = null;
        }
    }
}
