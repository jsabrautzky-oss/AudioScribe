using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AudioScribe.App.Models;

namespace AudioScribe.App.Services;

public interface IGpuAddonService
{
    event Action<double, string>? Progress;
    bool HasPackageConfigured { get; }
    Task<GpuAddonResult> InstallAsync(CancellationToken cancellationToken);
}

/// <summary>Lädt das CUDA-Laufzeit-Paket und die CUDA-Engine (GPU-Backend) und entpackt beide in den Engine-Ordner.</summary>
public sealed class GpuAddonService : IGpuAddonService
{
    private const string DefaultPackageUrl =
        "https://github.com/jsabrautzky-oss/AudioScribe/releases/download/cuda-runtime/audioscribe-cuda-windows-x64.zip";

    private const string DefaultBackendPackageUrl =
        "https://github.com/jsabrautzky-oss/AudioScribe/releases/download/v0.1.7/audioscribe-backend-cuda-windows-x64.zip";

    private readonly HardwareDetectorService _hardware;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(45) };

    public event Action<double, string>? Progress;

    public GpuAddonService(HardwareDetectorService hardware)
    {
        _hardware = hardware;
    }

    public bool HasPackageConfigured =>
        !string.IsNullOrWhiteSpace(ResolvePackageUrl()) &&
        !string.IsNullOrWhiteSpace(ResolveBackendPackageUrl());

    public async Task<GpuAddonResult> InstallAsync(CancellationToken cancellationToken)
    {
        var runtimeUrl = ResolvePackageUrl();
        var backendUrl = ResolveBackendPackageUrl();
        if (string.IsNullOrWhiteSpace(runtimeUrl) || string.IsNullOrWhiteSpace(backendUrl))
            return new GpuAddonResult(false, "Keine CUDA-Paket-URL konfiguriert.");

        try
        {
            await DownloadAndExtractAsync(runtimeUrl, "audioscribe-cuda", "CUDA-Laufzeit",
                0.0, 40.0, 50.0, cancellationToken);
            await DownloadAndExtractAsync(backendUrl, "audioscribe-backend-cuda", "GPU-Engine",
                50.0, 90.0, 100.0, cancellationToken);

            _hardware.RefreshCudaState();
            if (!_hardware.HasGpuEngine)
                return new GpuAddonResult(false,
                    "CUDA-Pakete wurden entpackt, aber CUDA-Laufzeit oder GPU-Engine wurde nicht gefunden.");

            Report(100, "GPU-Beschleunigung aktiv (CUDA)");
            return new GpuAddonResult(true, "GPU-Beschleunigung aktiv (CUDA)");
        }
        catch (OperationCanceledException)
        {
            return new GpuAddonResult(false, "CUDA-Download abgebrochen.");
        }
        catch (HttpRequestException ex)
        {
            return new GpuAddonResult(false, $"CUDA-Download fehlgeschlagen: {ex.Message}");
        }
        catch (Exception ex)
        {
            return new GpuAddonResult(false, $"CUDA-Installation fehlgeschlagen: {ex.Message}");
        }
    }

    private async Task DownloadAndExtractAsync(
        string url, string filePrefix, string label,
        double downloadStart, double downloadEnd, double extractEnd,
        CancellationToken cancellationToken)
    {
        var downloadDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AudioScribe", "downloads");
        Directory.CreateDirectory(downloadDir);
        var zipPath = Path.Combine(downloadDir, $"{filePrefix}-{DateTime.Now:yyyyMMdd-HHmmss}.zip");

        try
        {
            Report(downloadStart, $"{label} wird vorbereitet...");
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? -1L;
            await using var remote = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var local = File.Create(zipPath);

            var buffer = new byte[128 * 1024];
            long received = 0;
            int read;
            while ((read = await remote.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await local.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                received += read;

                var message = total > 0
                    ? $"{label} wird heruntergeladen ({received / 1_048_576.0:0.0} / {total / 1_048_576.0:0.0} MB)"
                    : $"{label} wird heruntergeladen ({received / 1_048_576.0:0.0} MB)";
                Report(Span(downloadStart, downloadEnd, total > 0 ? (double)received / total : 0.0), message);
            }

            await local.FlushAsync(cancellationToken);
            local.Close();

            Report(downloadEnd, $"{label} wird entpackt...");
            ExtractZipToEngine(zipPath, label, downloadEnd, extractEnd);
        }
        finally
        {
            TryDelete(zipPath);
        }
    }

    private void ExtractZipToEngine(string zipPath, string label, double progressStart, double progressEnd)
    {
        var engineDir = Path.GetFullPath(_hardware.EngineDirectory);
        Directory.CreateDirectory(engineDir);

        using var archive = ZipFile.OpenRead(zipPath);
        var totalEntries = Math.Max(1, archive.Entries.Count);
        int index = 0;

        foreach (var entry in archive.Entries)
        {
            index++;
            if (string.IsNullOrEmpty(entry.Name))
                continue;

            var destination = Path.GetFullPath(Path.Combine(engineDir, entry.FullName));
            if (!destination.StartsWith(engineDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                continue;

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: true);
            Report(Span(progressStart, progressEnd, (double)index / totalEntries),
                $"{label} wird entpackt ({index}/{totalEntries})...");
        }
    }

    private static double Span(double start, double end, double ratio) =>
        start + (end - start) * Math.Clamp(ratio, 0.0, 1.0);

    private string? ResolvePackageUrl() =>
        ResolveUrl("AUDIOSCRIBE_CUDA_PACKAGE_URL", "cuda_package_url.txt", DefaultPackageUrl);

    private string? ResolveBackendPackageUrl() =>
        ResolveUrl("AUDIOSCRIBE_CUDA_BACKEND_URL", "cuda_backend_package_url.txt", DefaultBackendPackageUrl);

    private static string? ResolveUrl(string envName, string fileName, string defaultUrl)
    {
        var env = Environment.GetEnvironmentVariable(envName);
        if (!string.IsNullOrWhiteSpace(env))
            return env.Trim();

        var candidates = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", fileName),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AudioScribe", fileName),
        };

        foreach (var candidate in candidates)
        {
            try
            {
                if (!File.Exists(candidate))
                    continue;

                var url = File.ReadAllText(candidate).Trim();
                if (!string.IsNullOrWhiteSpace(url))
                    return url;
            }
            catch
            {
                continue;
            }
        }

        return defaultUrl;
    }

    private void Report(double percent, string message) =>
        Progress?.Invoke(Math.Clamp(percent, 0.0, 100.0), message);

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Aufräumen ist best effort.
        }
    }
}
