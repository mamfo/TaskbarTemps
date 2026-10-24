using System;
using System.IO;
using System.Text;
using System.Security.Principal;
using LibreHardwareMonitor.Hardware;

public class UpdateVisitor : IVisitor
{
    public void VisitComputer(IComputer computer) { computer.Traverse(this); }
    public void VisitHardware(IHardware hardware)
    {
        hardware.Update();
        foreach (IHardware sub in hardware.SubHardware) sub.Accept(this);
    }
    public void VisitSensor(ISensor sensor) { }
    public void VisitParameter(IParameter parameter) { }
}

public static class SensorDump
{
    static StringBuilder sb = new StringBuilder();

    static void Dump(IHardware h, int depth)
    {
        string pad = new string(' ', depth * 2);
        sb.AppendLine(pad + "[" + h.HardwareType + "] " + h.Name + "  (id=" + h.Identifier + ")");
        foreach (IHardware sub in h.SubHardware)
        {
            Dump(sub, depth + 1);
        }
        foreach (ISensor s in h.Sensors)
        {
            sb.AppendLine(pad + "  " + s.SensorType + " | " + s.Name + " = " +
                (s.Value.HasValue ? s.Value.Value.ToString("0.##") : "null") + " " + s.Identifier);
        }
    }

    public static void Main()
    {
        bool admin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        sb.AppendLine("Admin: " + admin);
        sb.AppendLine("OS: " + Environment.OSVersion + " 64bit=" + Environment.Is64BitProcess);

        Computer c = new Computer();
        c.IsCpuEnabled = true;
        c.IsGpuEnabled = true;
        c.IsMemoryEnabled = true;
        c.IsMotherboardEnabled = true;
        c.IsStorageEnabled = false;
        c.IsNetworkEnabled = false;
        c.IsControllerEnabled = false;

        try
        {
            c.Open();
            sb.AppendLine("Computer opened OK");
        }
        catch (Exception ex)
        {
            sb.AppendLine("OPEN ERROR: " + ex);
        }

        try
        {
            c.Accept(new UpdateVisitor());
        }
        catch (Exception ex)
        {
            sb.AppendLine("UPDATE ERROR: " + ex);
        }

        foreach (IHardware h in c.Hardware)
        {
            try { Dump(h, 0); } catch (Exception ex) { sb.AppendLine("DUMP ERROR: " + ex.Message); }
        }

        try { c.Close(); } catch { }

        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sensors.txt");
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        Console.Write(sb.ToString());
    }
}
