using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Principal;
using System.Text;
using System.Threading;
using LibreHardwareMonitor.Hardware;

namespace TaskbarTemps
{
    internal sealed class SensorRow
    {
        public string Hardware;
        public string Type;
        public string Name;
        public string Value;
    }

    internal sealed class SensorSnapshot
    {
        public bool IsAdmin;
        public string Error = string.Empty;
        public DateTime Updated = DateTime.MinValue;

        public string CpuName = "CPU";
        public float? CpuTemp;
        public string CpuTempSource = string.Empty;
        public float? CpuLoad;
        public float? CpuPower;

        public string RamName = "RAM";
        public float? RamTemp;
        public string RamTempSource = string.Empty;
        public float? RamLoad;
        public float? RamTotalGb;

        public string GpuName = "GPU";
        public float? GpuTemp;
        public string GpuTempSource = string.Empty;
        public float? GpuHotspot;
        public float? GpuLoad;
        public float? GpuFan;
        public float? GpuPower;
        public float? GpuMemUsedMb;
        public float? GpuMemTotalMb;

        public string NetName = "Rede";
        public bool NetAvailable;
        public double? NetDownBps;
        public double? NetUpBps;

        public bool DiskAvailable;
        public double? DiskReadBps;
        public double? DiskWriteBps;

        public List<SensorRow> Rows = new List<SensorRow>();
    }

    internal sealed class HardwareMonitor : IDisposable
    {
        private struct SensorRef
        {
            public ISensor Sensor;
            public IHardware Hardware;
        }

        private sealed class UpdateVisitor : IVisitor
        {
            public void VisitComputer(IComputer computer)
            {
                computer.Traverse(this);
            }

            public void VisitHardware(IHardware hardware)
            {
                try
                {
                    hardware.Update();
                }
                catch
                {
                    // um controlador com problemas não deve impedir os restantes sensores
                }
                foreach (IHardware sub in hardware.SubHardware)
                {
                    sub.Accept(this);
                }
            }

            public void VisitSensor(ISensor sensor) { }
            public void VisitParameter(IParameter parameter) { }
        }

        private readonly Computer _computer;
        private readonly UpdateVisitor _visitor = new UpdateVisitor();
        private readonly NetworkUsage _network = new NetworkUsage();
        private readonly DiskUsage _disk = new DiskUsage();
        private readonly object _gate = new object();
        private readonly ManualResetEvent _wake = new ManualResetEvent(false);
        private readonly bool _isAdmin;

        private SensorSnapshot _latest = new SensorSnapshot();
        private Thread _thread;
        private volatile bool _running;
        private volatile int _intervalMs = 1000;
        private bool _opened;
        private DateTime _nextOpenAttempt = DateTime.MinValue;
        private bool _disposed;

        public HardwareMonitor()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                _isAdmin = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }

            _computer = new Computer();
            _computer.IsCpuEnabled = true;
            _computer.IsGpuEnabled = true;
            _computer.IsMemoryEnabled = true;
            _computer.IsMotherboardEnabled = true;
            _computer.IsControllerEnabled = false;
            _computer.IsNetworkEnabled = false;
            _computer.IsStorageEnabled = false;

            _latest.IsAdmin = _isAdmin;
        }

        public SensorSnapshot Latest
        {
            get
            {
                lock (_gate)
                {
                    return _latest;
                }
            }
        }

        public void SetInterval(int milliseconds)
        {
            _intervalMs = Math.Max(500, Math.Min(10000, milliseconds));
            _wake.Set();
        }

        public void Start()
        {
            if (_thread != null) return;
            _running = true;
            _thread = new Thread(Loop);
            _thread.IsBackground = true;
            _thread.Name = "TaskbarTemps.Sensores";
            _thread.Start();
        }

        private void Loop()
        {
            while (_running)
            {
                SensorSnapshot snapshot;
                try
                {
                    snapshot = ReadSnapshot();
                }
                catch (Exception ex)
                {
                    snapshot = new SensorSnapshot();
                    snapshot.IsAdmin = _isAdmin;
                    snapshot.Updated = DateTime.Now;
                    snapshot.Error = ex.Message;
                    Log.Write("Erro ao ler sensores: " + ex);
                }

                lock (_gate)
                {
                    _latest = snapshot;
                }

                _wake.WaitOne(_intervalMs, false);
                _wake.Reset();
            }
        }

        private SensorSnapshot ReadSnapshot()
        {
            SensorSnapshot snap = new SensorSnapshot();
            snap.IsAdmin = _isAdmin;
            snap.Updated = DateTime.Now;

            // A rede é independente dos sensores de hardware e funciona
            // mesmo antes de o LibreHardwareMonitor conseguir abrir.
            _network.Sample();
            snap.NetName = _network.Name;
            snap.NetAvailable = _network.Available;
            if (_network.Available)
            {
                snap.NetDownBps = _network.DownloadBps;
                snap.NetUpBps = _network.UploadBps;
            }

            // O disco também é independente dos sensores: fica disponível
            // mesmo que o LibreHardwareMonitor ainda não tenha aberto.
            _disk.Sample();
            snap.DiskAvailable = _disk.Available;
            if (_disk.Available)
            {
                snap.DiskReadBps = _disk.ReadBps;
                snap.DiskWriteBps = _disk.WriteBps;
            }

            if (!_opened)
            {
                if (DateTime.Now < _nextOpenAttempt)
                {
                    snap.Error = "A iniciar sensores…";
                    return snap;
                }
                try
                {
                    _computer.Open();
                    _opened = true;
                }
                catch (Exception ex)
                {
                    _nextOpenAttempt = DateTime.Now.AddSeconds(15);
                    snap.Error = ex.Message;
                    Log.Write("Falha ao abrir sensores: " + ex);
                    return snap;
                }
            }

            try
            {
                _computer.Accept(_visitor);
            }
            catch (Exception ex)
            {
                snap.Error = ex.Message;
                Log.Write("Falha ao atualizar sensores: " + ex.Message);
            }

            List<IHardware> all = new List<IHardware>();
            List<IHardware> cpus = new List<IHardware>();
            List<IHardware> gpus = new List<IHardware>();
            List<IHardware> memories = new List<IHardware>();

            foreach (IHardware hardware in _computer.Hardware)
            {
                Collect(hardware, all, cpus, gpus, memories);
            }

            ReadCpu(snap, cpus);
            ReadGpu(snap, gpus);
            ReadRam(snap, memories, all);
            snap.Rows = BuildRows(all);
            AppendDiskRows(snap);
            AppendNetworkRows(snap);

            return snap;
        }

        private static void AppendDiskRows(SensorSnapshot snap)
        {
            if (!snap.DiskAvailable) return;

            SensorRow read = new SensorRow();
            read.Hardware = "Disco";
            read.Type = "Disco";
            read.Name = "Leitura";
            read.Value = DisplayFormat.Rate(snap.DiskReadBps);

            SensorRow write = new SensorRow();
            write.Hardware = "Disco";
            write.Type = "Disco";
            write.Name = "Escrita";
            write.Value = DisplayFormat.Rate(snap.DiskWriteBps);

            snap.Rows.Add(read);
            snap.Rows.Add(write);
        }

        private static void AppendNetworkRows(SensorSnapshot snap)
        {
            if (!snap.NetAvailable) return;

            SensorRow down = new SensorRow();
            down.Hardware = snap.NetName;
            down.Type = "Rede";
            down.Name = "Receção";
            down.Value = DisplayFormat.Rate(snap.NetDownBps);

            SensorRow up = new SensorRow();
            up.Hardware = snap.NetName;
            up.Type = "Rede";
            up.Name = "Envio";
            up.Value = DisplayFormat.Rate(snap.NetUpBps);

            snap.Rows.Add(down);
            snap.Rows.Add(up);
        }

        private static void Collect(IHardware hardware, List<IHardware> all, List<IHardware> cpus,
            List<IHardware> gpus, List<IHardware> memories)
        {
            all.Add(hardware);

            HardwareType type = hardware.HardwareType;
            if (type == HardwareType.Cpu)
            {
                cpus.Add(hardware);
            }
            else if (type == HardwareType.GpuNvidia || type == HardwareType.GpuAmd || type == HardwareType.GpuIntel)
            {
                gpus.Add(hardware);
            }
            else if (type == HardwareType.Memory)
            {
                memories.Add(hardware);
            }

            foreach (IHardware sub in hardware.SubHardware)
            {
                Collect(sub, all, cpus, gpus, memories);
            }
        }

        private static void ReadCpu(SensorSnapshot snap, List<IHardware> cpus)
        {
            if (cpus.Count == 0) return;
            snap.CpuName = cpus[0].Name;

            List<SensorRef> temperatures = GatherTemperatures(cpus);
            if (temperatures.Count > 0)
            {
                SensorRef? pick = PickExact(temperatures, "CPU Package");
                if (!pick.HasValue) pick = PickContains(temperatures, "Tctl/Tdie");
                if (!pick.HasValue) pick = PickContains(temperatures, "Tdie");
                if (!pick.HasValue) pick = PickExact(temperatures, "Core Average");
                if (!pick.HasValue) pick = PickContains(temperatures, "Core Max");

                if (pick.HasValue)
                {
                    snap.CpuTemp = pick.Value.Sensor.Value;
                    snap.CpuTempSource = pick.Value.Sensor.Name;
                }
                else
                {
                    float sum = 0f;
                    int count = 0;
                    foreach (SensorRef r in temperatures)
                    {
                        string name = r.Sensor.Name;
                        if (name.IndexOf("Core", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        if (name.IndexOf("Distance", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        if (name.IndexOf("Max", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        if (name.IndexOf("Average", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        sum += r.Sensor.Value.Value;
                        count++;
                    }
                    if (count > 0)
                    {
                        snap.CpuTemp = sum / count;
                        snap.CpuTempSource = "média de " + count.ToString(CultureInfo.CurrentCulture) + " núcleos";
                    }
                    else
                    {
                        float best = float.MinValue;
                        foreach (SensorRef r in temperatures)
                        {
                            if (r.Sensor.Value.Value > best)
                            {
                                best = r.Sensor.Value.Value;
                                snap.CpuTemp = best;
                                snap.CpuTempSource = r.Sensor.Name;
                            }
                        }
                    }
                }
            }

            foreach (IHardware cpu in cpus)
            {
                if (!snap.CpuLoad.HasValue)
                {
                    ISensor load = FindSensor(cpu.Sensors, SensorType.Load, "CPU Total");
                    if (load == null) load = FindContains(cpu.Sensors, SensorType.Load, "CPU Total");
                    if (load != null) snap.CpuLoad = load.Value;
                }
                if (!snap.CpuPower.HasValue)
                {
                    ISensor power = FindSensor(cpu.Sensors, SensorType.Power, "CPU Package");
                    if (power == null) power = FindSensor(cpu.Sensors, SensorType.Power, "Package");
                    if (power != null) snap.CpuPower = power.Value;
                }
            }
        }

        private static void ReadGpu(SensorSnapshot snap, List<IHardware> gpus)
        {
            if (gpus.Count == 0) return;

            IHardware chosen = null;

            foreach (IHardware gpu in gpus)
            {
                bool dedicated = gpu.HardwareType == HardwareType.GpuNvidia || gpu.HardwareType == HardwareType.GpuAmd;
                if (dedicated && FindSensor(gpu.Sensors, SensorType.Temperature, "GPU Core") != null)
                {
                    chosen = gpu;
                    break;
                }
            }
            if (chosen == null)
            {
                foreach (IHardware gpu in gpus)
                {
                    if (FindSensor(gpu.Sensors, SensorType.Temperature, "GPU Core") != null)
                    {
                        chosen = gpu;
                        break;
                    }
                }
            }
            if (chosen == null)
            {
                foreach (IHardware gpu in gpus)
                {
                    if (gpu.HardwareType == HardwareType.GpuNvidia || gpu.HardwareType == HardwareType.GpuAmd)
                    {
                        chosen = gpu;
                        break;
                    }
                }
            }
            if (chosen == null) chosen = gpus[0];

            snap.GpuName = chosen.Name;

            ISensor core = FindSensor(chosen.Sensors, SensorType.Temperature, "GPU Core");
            if (core == null) core = FindContains(chosen.Sensors, SensorType.Temperature, "GPU Core");
            if (core != null)
            {
                snap.GpuTemp = core.Value;
                snap.GpuTempSource = core.Name;
            }
            else
            {
                float best = float.MinValue;
                foreach (ISensor s in chosen.Sensors)
                {
                    if (s.SensorType != SensorType.Temperature || !s.Value.HasValue) continue;
                    if (s.Value.Value > best)
                    {
                        best = s.Value.Value;
                        snap.GpuTemp = best;
                        snap.GpuTempSource = s.Name;
                    }
                }
            }

            ISensor hotspot = FindSensor(chosen.Sensors, SensorType.Temperature, "GPU Hot Spot");
            if (hotspot != null) snap.GpuHotspot = hotspot.Value;

            ISensor load = FindSensor(chosen.Sensors, SensorType.Load, "GPU Core");
            if (load == null) load = FindSensor(chosen.Sensors, SensorType.Load, "D3D 3D");
            if (load != null) snap.GpuLoad = load.Value;

            foreach (ISensor s in chosen.Sensors)
            {
                if (s.SensorType == SensorType.Fan && s.Value.HasValue)
                {
                    snap.GpuFan = s.Value;
                    break;
                }
            }

            ISensor gpuPower = FindSensor(chosen.Sensors, SensorType.Power, "GPU Package");
            if (gpuPower == null) gpuPower = FindSensor(chosen.Sensors, SensorType.Power, "GPU Power");
            if (gpuPower != null) snap.GpuPower = gpuPower.Value;

            ISensor memUsed = FindSensor(chosen.Sensors, SensorType.SmallData, "GPU Memory Used");
            ISensor memTotal = FindSensor(chosen.Sensors, SensorType.SmallData, "GPU Memory Total");
            if (memUsed != null) snap.GpuMemUsedMb = memUsed.Value;
            if (memTotal != null) snap.GpuMemTotalMb = memTotal.Value;
        }

        private static void ReadRam(SensorSnapshot snap, List<IHardware> memories, List<IHardware> all)
        {
            IHardware total = null;
            foreach (IHardware memory in memories)
            {
                if (memory.Name.IndexOf("Total", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    total = memory;
                    break;
                }
            }
            if (total == null && memories.Count > 0) total = memories[0];

            if (total != null)
            {
                snap.RamName = "RAM";
                ISensor load = FindSensor(total.Sensors, SensorType.Load, "Memory");
                if (load != null) snap.RamLoad = load.Value;

                ISensor used = FindSensor(total.Sensors, SensorType.Data, "Memory Used");
                ISensor available = FindSensor(total.Sensors, SensorType.Data, "Memory Available");
                if (used != null)
                {
                    float totalGb = used.Value.Value + (available != null ? available.Value.Value : 0f);
                    if (totalGb > 0f) snap.RamTotalGb = totalGb;
                }
            }

            // A temperatura da RAM só existe em módulos com sensor SPD (habitualmente DDR5).
            float bestTemp = float.MinValue;
            foreach (IHardware hardware in all)
            {
                foreach (ISensor sensor in hardware.Sensors)
                {
                    if (sensor.SensorType != SensorType.Temperature || !sensor.Value.HasValue) continue;
                    bool isMemorySensor = hardware.HardwareType == HardwareType.Memory
                        || sensor.Name.IndexOf("DIMM", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!isMemorySensor) continue;
                    if (sensor.Value.Value > bestTemp)
                    {
                        bestTemp = sensor.Value.Value;
                        snap.RamTemp = bestTemp;
                        snap.RamTempSource = sensor.Name;
                    }
                }
            }
        }

        private static List<SensorRef> GatherTemperatures(List<IHardware> hardwareList)
        {
            List<SensorRef> result = new List<SensorRef>();
            foreach (IHardware hardware in hardwareList)
            {
                foreach (ISensor sensor in hardware.Sensors)
                {
                    if (sensor.SensorType != SensorType.Temperature || !sensor.Value.HasValue) continue;
                    SensorRef r = new SensorRef();
                    r.Sensor = sensor;
                    r.Hardware = hardware;
                    result.Add(r);
                }
            }
            return result;
        }

        private static SensorRef? PickExact(List<SensorRef> sensors, string name)
        {
            foreach (SensorRef r in sensors)
            {
                if (string.Equals(r.Sensor.Name, name, StringComparison.OrdinalIgnoreCase)) return r;
            }
            return null;
        }

        private static SensorRef? PickContains(List<SensorRef> sensors, string text)
        {
            foreach (SensorRef r in sensors)
            {
                if (r.Sensor.Name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0) return r;
            }
            return null;
        }

        private static ISensor FindSensor(IEnumerable<ISensor> sensors, SensorType type, string name)
        {
            foreach (ISensor sensor in sensors)
            {
                if (sensor.SensorType == type
                    && sensor.Value.HasValue
                    && string.Equals(sensor.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return sensor;
                }
            }
            return null;
        }

        private static ISensor FindContains(IEnumerable<ISensor> sensors, SensorType type, string text)
        {
            foreach (ISensor sensor in sensors)
            {
                if (sensor.SensorType == type
                    && sensor.Value.HasValue
                    && sensor.Name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return sensor;
                }
            }
            return null;
        }

        private static List<SensorRow> BuildRows(List<IHardware> all)
        {
            List<SensorRow> rows = new List<SensorRow>();
            foreach (IHardware hardware in all)
            {
                foreach (ISensor sensor in hardware.Sensors)
                {
                    if (!sensor.Value.HasValue) continue;
                    SensorRow row = new SensorRow();
                    row.Hardware = hardware.Name;
                    row.Type = TypeLabel(sensor.SensorType);
                    row.Name = sensor.Name;
                    row.Value = FormatValue(sensor);
                    rows.Add(row);
                    if (rows.Count >= 500) return rows;
                }
            }
            return rows;
        }

        private static string TypeLabel(SensorType type)
        {
            switch (type)
            {
                case SensorType.Temperature: return "Temperatura";
                case SensorType.Load: return "Carga";
                case SensorType.Fan: return "Ventoinha";
                case SensorType.Power: return "Potência";
                case SensorType.Clock: return "Frequência";
                case SensorType.Voltage: return "Tensão";
                case SensorType.Data: return "Dados";
                case SensorType.SmallData: return "Dados";
                case SensorType.Throughput: return "Débito";
                case SensorType.Control: return "Controlo";
                case SensorType.Level: return "Nível";
                case SensorType.Frequency: return "Frequência";
                case SensorType.Humidity: return "Humidade";
                case SensorType.Flow: return "Fluxo";
                case SensorType.Conductivity: return "Condutividade";
                default: return type.ToString();
            }
        }

        private static string FormatValue(ISensor sensor)
        {
            float value = sensor.Value.HasValue ? sensor.Value.Value : 0f;
            CultureInfo ci = CultureInfo.CurrentCulture;
            switch (sensor.SensorType)
            {
                case SensorType.Temperature: return value.ToString("0.0", ci) + " °C";
                case SensorType.Load: return value.ToString("0.0", ci) + " %";
                case SensorType.Control: return value.ToString("0", ci) + " %";
                case SensorType.Level: return value.ToString("0", ci) + " %";
                case SensorType.Fan: return value.ToString("0", ci) + " RPM";
                case SensorType.Power: return value.ToString("0.0", ci) + " W";
                case SensorType.Clock: return value.ToString("0", ci) + " MHz";
                case SensorType.Voltage: return value.ToString("0.000", ci) + " V";
                case SensorType.Throughput: return (value / 1024f).ToString("0.0", ci) + " KB/s";
                case SensorType.Frequency: return value.ToString("0", ci) + " Hz";
                default: return value.ToString("0.##", ci);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _running = false;
            _wake.Set();
            if (_thread != null && _thread.IsAlive)
            {
                try { _thread.Join(2500); } catch { }
            }
            try
            {
                if (_opened) _computer.Close();
            }
            catch { }
            _network.Dispose();
            _disk.Dispose();
            _wake.Close();
        }
    }
}
