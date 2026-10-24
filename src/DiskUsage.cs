using System;
using System.Diagnostics;

namespace TaskbarTemps
{
    /// <summary>
    /// Mede a velocidade de leitura e escrita dos discos.
    /// Usa os contadores de desempenho PhysicalDisk ("_Total"), que medem o
    /// tráfego real dos dispositivos, e recorre aos LogicalDisk ("_Total")
    /// quando os primeiros não estão disponíveis.
    /// </summary>
    internal sealed class DiskUsage : IDisposable
    {
        private PerformanceCounter _read;
        private PerformanceCounter _write;
        private bool _tried;

        public bool Available;
        public double ReadBps;
        public double WriteBps;

        public void Sample()
        {
            if (!_tried)
            {
                _tried = true;
                TryOpen("PhysicalDisk");
                if (_read == null || _write == null) TryOpen("LogicalDisk");
            }

            if (_read == null || _write == null)
            {
                Available = false;
                ReadBps = 0d;
                WriteBps = 0d;
                return;
            }

            try
            {
                double read = _read.NextValue();
                double write = _write.NextValue();
                Available = true;
                ReadBps = read < 0d ? 0d : read;
                WriteBps = write < 0d ? 0d : write;
            }
            catch
            {
                Available = false;
                ReadBps = 0d;
                WriteBps = 0d;
                CloseCounters();
            }
        }

        private void TryOpen(string category)
        {
            CloseCounters();
            PerformanceCounter read = null;
            PerformanceCounter write = null;
            try
            {
                read = new PerformanceCounter(category, "Disk Read Bytes/sec", "_Total", true);
                write = new PerformanceCounter(category, "Disk Write Bytes/sec", "_Total", true);
                read.NextValue();
                write.NextValue();
                _read = read;
                _write = write;
            }
            catch
            {
                if (read != null) read.Dispose();
                if (write != null) write.Dispose();
                _read = null;
                _write = null;
            }
        }

        private void CloseCounters()
        {
            try
            {
                if (_read != null) _read.Dispose();
            }
            catch
            {
            }
            try
            {
                if (_write != null) _write.Dispose();
            }
            catch
            {
            }
            _read = null;
            _write = null;
        }

        public void Dispose()
        {
            CloseCounters();
        }
    }
}
