using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace AudioScribe.App.Services;

/// <summary>
/// Ermittelt freien/gesamten System-RAM (GlobalMemoryStatusEx) sowie bei NVIDIA-GPUs
/// den dedizierten Grafikspeicher (nvidia-smi-Abfrage, NVML-basiert).
/// </summary>
public sealed class SystemMemoryService
{
    public double TotalMemoryGb { get; private set; }
    public double FreeMemoryGb { get; private set; }

    public double TotalVramGb { get; private set; }
    public double FreeVramGb { get; private set; }

    /// <summary>True, wenn die letzte VRAM-Abfrage erfolgreiche Werte geliefert hat.</summary>
    public bool HasVramInfo { get; private set; }

    public SystemMemoryService() => RefreshSystemMemory();

    public void RefreshSystemMemory()
    {
        var status = new MemoryStatusEx();
        if (GlobalMemoryStatusEx(ref status))
        {
            TotalMemoryGb = status.UllTotalPhys / (1024.0 * 1024.0 * 1024.0);
            FreeMemoryGb = status.UllAvailPhys / (1024.0 * 1024.0 * 1024.0);
        }
    }

    public async Task RefreshAsync()
    {
        RefreshSystemMemory();
        var vram = await QueryVramAsync();
        if (vram is (double free, double total) && total > 0)
        {
            FreeVramGb = free;
            TotalVramGb = total;
            HasVramInfo = true;
        }
        else
        {
            HasVramInfo = false;
        }
    }

    private static async Task<(double Free, double Total)?> QueryVramAsync()
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "nvidia-smi",
                    Arguments = "--query-gpu=memory.free,memory.total --format=csv,noheader,nounits",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
            };
            process.Start();
            Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
            Task exitTask = process.WaitForExitAsync();
            if (await Task.WhenAny(exitTask, Task.Delay(3000)) != exitTask)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* bereits beendet */ }
                return null;
            }
            await exitTask;
            if (process.ExitCode != 0)
                return null;

            var output = await outputTask;
            double freeSum = 0;
            double totalSum = 0;
            int gpus = 0;
            foreach (var rawLine in output.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.Length == 0)
                    continue;
                var parts = line.Split(',');
                if (parts.Length < 2)
                    continue;
                if (!double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var freeMb))
                    continue;
                if (!double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var totalMb))
                    continue;
                freeSum += freeMb;
                totalSum += totalMb;
                gpus++;
            }
            if (gpus == 0)
                return null;
            return (freeSum / 1024.0, totalSum / 1024.0);
        }
        catch (Exception)
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint DwLength;
        public uint DwMemoryLoad;
        public ulong UllTotalPhys;
        public ulong UllAvailPhys;
        public ulong UllTotalPageFile;
        public ulong UllAvailPageFile;
        public ulong UllTotalVirtual;
        public ulong UllAvailVirtual;
        public ulong UllAvailExtendedVirtual;

        public MemoryStatusEx()
        {
            DwLength = (uint)Marshal.SizeOf<MemoryStatusEx>();
            DwMemoryLoad = 0;
            UllTotalPhys = 0;
            UllAvailPhys = 0;
            UllTotalPageFile = 0;
            UllAvailPageFile = 0;
            UllTotalVirtual = 0;
            UllAvailVirtual = 0;
            UllAvailExtendedVirtual = 0;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);
}
