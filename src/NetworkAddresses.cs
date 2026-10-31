using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace TaskbarTemps
{
    internal static class NetworkAddresses
    {
        private static readonly string[] IgnoredHints = new string[]
        {
            "loopback", "isatap", "teredo", "pseudo", "vethernet", "virtual",
            "vmware", "virtualbox", "hyper-v", "tap-", "tun", "vpn", "wsl",
            "bluetooth", "npcap", "wireguard", "zerotier"
        };

        /// <summary>
        /// Devolve o IPv4 da rede local, dando preferência à placa com gateway
        /// predefinido (a que tem ligação efetiva à Internet).
        /// </summary>
        public static string LocalIPv4()
        {
            try
            {
                string withGateway = null;
                string any = null;

                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    if (IsIgnored(ni.Name + " " + ni.Description)) continue;

                    IPInterfaceProperties properties;
                    try { properties = ni.GetIPProperties(); }
                    catch { continue; }

                    string address = FirstIPv4(properties);
                    if (string.IsNullOrEmpty(address)) continue;

                    if (HasGateway(properties))
                    {
                        if (withGateway == null) withGateway = address;
                    }
                    else if (any == null)
                    {
                        any = address;
                    }
                }

                if (withGateway != null) return withGateway;
                return any ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string FirstIPv4(IPInterfaceProperties properties)
        {
            try
            {
                foreach (UnicastIPAddressInformation unicast in properties.UnicastAddresses)
                {
                    if (unicast.Address == null) continue;
                    if (unicast.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    if (IPAddress.IsLoopback(unicast.Address)) continue;
                    string text = unicast.Address.ToString();
                    if (text.StartsWith("169.254.", StringComparison.Ordinal)) continue;
                    return text;
                }
            }
            catch
            {
            }
            return null;
        }

        private static bool HasGateway(IPInterfaceProperties properties)
        {
            try
            {
                foreach (GatewayIPAddressInformation gateway in properties.GatewayAddresses)
                {
                    if (gateway.Address == null) continue;
                    if (gateway.Address.AddressFamily == AddressFamily.InterNetwork) return true;
                }
            }
            catch
            {
            }
            return false;
        }

        private static bool IsIgnored(string text)
        {
            string lower = text.ToLowerInvariant();
            foreach (string hint in IgnoredHints)
            {
                if (lower.IndexOf(hint, StringComparison.Ordinal) >= 0) return true;
            }
            return false;
        }
    }
}
