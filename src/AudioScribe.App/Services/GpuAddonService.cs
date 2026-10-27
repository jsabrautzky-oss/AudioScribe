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

/// <summary>Lädt ein optionales CUDA-Runtime-Zip und entpackt es in den Engine-Ordner.</summary>
public sealed class GpuAddonService : IGpuAddonService
{
    private const string DefaultPackageUrl =
        "https://github.com/jsabrautzky-oss/AudioScribe/releases/download/cuda-runtime/audioscribe-cuda-windows-x64.zip";

    private readonly HardwareDetectorService _hardware;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(45) };

    public event Action<double, string>? Progress;

    public GpuAddonService(HardwareDetectorService hardware)
    {
        _hardware = hardware;
    }

    public bool HasPackageConfigured => !string.IsNullOrWhiteSpace(ResolvePackageUrl());

    public async Task<GpuAddonResult> InstallAsync(CancellationToken cancellationToken)
    {
        var url = ResolvePackageUrl();
        if (string.IsNullOrWhiteSpace(url))
            return new GpuAddonResult(false, "Keine CUDA-Paket-URL konfiguriert.");

        var downloadDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AudioScribe", "downloads");
        Directory.CreateDirectory(downloadDir);
        var zipPath = Path.Combine(downloadDir, $"audioscribe-cuda-{DateTime.Now:yyyyMMdd-HHmmss}.zip");

        try
        {
            Report(0, "CUDA-Paket wird vorbereitet...");
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
                    ? $"CUDA-Paket wird heruntergeladen ({received / 1_048_576.0:0.0} / {total / 1_048_576.0:0.0} MB)"
                    : $"CUDA-Paket wird heruntergeladen ({received / 1_048_576.0:0.0} MB)";
                Report(total > 0 ? 90.0 * received / total : 0.0, message);
            }

            await local.FlushAsync(cancellationToken);
            local.Close();

            Report(90, "CUDA-Paket wird entpackt...");
            ExtractZipToEngine(zipPath);

            _hardware.RefreshCudaState();
            if (!_hardware.HasCudaRuntime)
                return new GpuAddonResult(false, "CUDA-Paket wurde entpackt, aber keine CUDA-Bibliotheken gefunden.");

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
        finally
        {
            TryDelete(zipPath);
        }
    }

    private void ExtractZipToEngine(string zipPath)
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
            Report(90.0 + 10.0 * index / totalEntries, $"Entpacke CUDA-Bibliotheken ({index}/{totalEntries})...");
        }
    }

    private string? ResolvePackageUrl()
    {
        var env = Environment.GetEnvironmentVariable("AUDIOSCRIBE_CUDA_PACKAGE_URL");
        if (!string.IsNullOrWhiteSpace(env))
            return env.Trim();

        var candidates = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cuda_package_url.txt"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "cuda_package_url.txt"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AudioScribe", "cuda_package_url.txt"),
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

        return DefaultPackageUrl;
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
