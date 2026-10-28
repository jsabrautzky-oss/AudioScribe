using System.IO;
using System.Management;

namespace AudioScribe.App.Services;

/// <summary>Erkennt NVIDIA-GPUs und prüft, ob eine CUDA-Runtime lokal verfügbar ist.</summary>
public sealed class HardwareDetectorService
{
    private static readonly string[] CudaDllPatterns =
    [
        "cublas*.dll",
        "cudnn*.dll",
        "cusparse*.dll",
        "cusolver*.dll",
        "nvrtc*.dll",
        "cudart*.dll",
    ];

    public string EngineDirectory { get; }
    public string NvidiaGpuName { get; private set; } = string.Empty;
    public bool HasNvidiaGpu => !string.IsNullOrWhiteSpace(NvidiaGpuName);
    public bool HasCudaRuntime { get; private set; }
    public bool HasCudaBackend { get; private set; }

    /// <summary>GPU-Engine ist vollstaendig: CUDA-Laufzeit (engine\cuda) plus CUDA-Backend (engine\backend).</summary>
    public bool HasGpuEngine => HasCudaRuntime && HasCudaBackend;

    public string? CudaBackendExe { get; private set; }

    public HardwareDetectorService()
    {
        EngineDirectory = ResolveEngineDirectory();
        DetectNvidiaGpu();
        RefreshCudaState();
    }

    public void RefreshCudaState()
    {
        HasCudaRuntime = DetectCudaRuntime();
        CudaBackendExe = ResolveCudaBackendExe();
        HasCudaBackend = CudaBackendExe is not null;
    }

    private string? ResolveCudaBackendExe()
    {
        var candidate = Path.Combine(EngineDirectory, "backend", "audioscribe-backend.exe");
        return File.Exists(candidate) ? Path.GetFullPath(candidate) : null;
    }

    private static string ResolveEngineDirectory()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var portableMarker = Path.Combine(baseDir, "portable.txt");
        var localFallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AudioScribe", "engine");

        string engineDir;
        try
        {
            engineDir = File.Exists(portableMarker)
                ? Path.Combine(baseDir, "engine")
                : Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AudioScribe", "engine");
        }
        catch (Exception)
        {
            engineDir = localFallback;
        }

        try
        {
            Directory.CreateDirectory(engineDir);
            Directory.CreateDirectory(Path.Combine(engineDir, "cuda"));
        }
        catch (UnauthorizedAccessException)
        {
            engineDir = localFallback;
            Directory.CreateDirectory(engineDir);
            Directory.CreateDirectory(Path.Combine(engineDir, "cuda"));
        }

        return Path.GetFullPath(engineDir);
    }

    private void DetectNvidiaGpu()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
            foreach (var item in searcher.Get())
            {
                var name = item["Name"] as string;
                if (!string.IsNullOrWhiteSpace(name) &&
                    name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                {
                    NvidiaGpuName = name.Trim();
                    break;
                }
            }
        }
        catch
        {
            NvidiaGpuName = string.Empty;
        }
    }

    private bool DetectCudaRuntime()
    {
        var roots = new[]
        {
            EngineDirectory,
            Path.Combine(EngineDirectory, "cuda"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backend"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "engine"),
        };

        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
                continue;

            int found = 0;
            foreach (var pattern in CudaDllPatterns)
            {
                if (FindFirstFile(root, pattern) is not null)
                    found++;
            }

            if (found >= 3)
                return true;
        }

        return false;
    }

    private static string? FindFirstFile(string root, string pattern)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories))
                return file;
        }
        catch
        {
            return null;
        }

        return null;
    }
}
