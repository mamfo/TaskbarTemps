using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace TaskbarTemps
{
    /// <summary>
    /// Obtém o endereço IPv4 público através de serviços HTTP simples e mantém
    /// o valor em cache. Só contacta a Internet quando está ativo, para que o
    /// widget não faça pedidos externos sem necessidade.
    /// </summary>
    internal sealed class PublicIpLookup : IDisposable
    {
        private static readonly string[] Services = new string[]
        {
            "https://api.ipify.org",
            "https://ifconfig.me/ip",
            "https://icanhazip.com"
        };

        private static readonly TimeSpan RefreshEvery = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(2);

        private readonly object _gate = new object();
        private readonly ManualResetEvent _wake = new ManualResetEvent(false);
        private readonly Thread _thread;

        private volatile bool _enabled;
        private volatile bool _running = true;
        private string _value = string.Empty;
        private DateTime _fetched = DateTime.MinValue;
        private DateTime _nextAttempt = DateTime.MinValue;
        private bool _warned;

        static PublicIpLookup()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            catch
            {
            }
        }

        public PublicIpLookup()
        {
            _thread = new Thread(Loop);
            _thread.IsBackground = true;
            _thread.Name = "TaskbarTemps.IPPublico";
            _thread.Start();
        }

        public bool Enabled
        {
            get { return _enabled; }
            set
            {
                if (_enabled == value) return;
                _enabled = value;
                // ao ativar, tenta já obter o endereço
                if (value) _nextAttempt = DateTime.MinValue;
                _wake.Set();
            }
        }

        public string Current
        {
            get
            {
                lock (_gate)
                {
                    return _value;
                }
            }
        }

        private void Loop()
        {
            while (_running)
            {
                try
                {
                    if (_enabled
                        && DateTime.Now - _fetched > RefreshEvery
                        && DateTime.Now >= _nextAttempt)
                    {
                        string value = Fetch();
                        lock (_gate)
                        {
                            if (!string.IsNullOrEmpty(value))
                            {
                                _value = value;
                                _fetched = DateTime.Now;
                                _warned = false;
                            }
                            else
                            {
                                _nextAttempt = DateTime.Now + RetryAfter;
                            }
                        }

                        if (string.IsNullOrEmpty(value) && !_warned)
                        {
                            _warned = true;
                            Log.Write("Não foi possível obter o IP público.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Write("Erro ao obter o IP público: " + ex.Message);
                }

                _wake.WaitOne(TimeSpan.FromSeconds(5));
                _wake.Reset();
            }
        }

        private static string Fetch()
        {
            foreach (string service in Services)
            {
                try
                {
                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create(service);
                    request.Method = "GET";
                    request.Timeout = 6000;
                    request.ReadWriteTimeout = 6000;
                    request.UserAgent = "TaskbarTemps";
                    request.Proxy = WebRequest.DefaultWebProxy;

                    using (WebResponse response = request.GetResponse())
                    using (Stream stream = response.GetResponseStream())
                    using (StreamReader reader = new StreamReader(stream, Encoding.ASCII))
                    {
                        string text = reader.ReadToEnd().Trim();
                        IPAddress address;
                        if (IPAddress.TryParse(text, out address)
                            && address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            return address.ToString();
                        }
                    }
                }
                catch
                {
                    // tenta o serviço seguinte
                }
            }
            return null;
        }

        public void Dispose()
        {
            _running = false;
            _wake.Set();
            try
            {
                if (_thread != null && _thread.IsAlive) _thread.Join(1500);
            }
            catch
            {
            }
            _wake.Close();
        }
    }
}
