using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using AudioScribe.App.Models;

namespace AudioScribe.App.Services;

/// <summary>
/// Führt das Python-Backend als Subprozess und parst das zeilenbasierte JSON-Protokoll von STDOUT.
/// Phase-A-Gerüst: Prozess-Handling, UTF-8, Kill(entireProcessTree) — Event-Auswertung bereits aktiv.
/// </summary>
public sealed class BackendRunnerService : IBackendService
{
    private readonly object _gate = new();
    private readonly HardwareDetectorService _hardware;
    private Process? _process;

    public BackendRunnerService(HardwareDetectorService hardware)
    {
        _hardware = hardware;
    }

    public event Action<ProcessProgress>? ProgressChanged;
    public event Action<BackendResult>? Completed;
    public event Action<string, string?>? Failed;
    public event Action<string>? Info;

    public bool IsRunning
    {
        get { lock (_gate) return _process is { HasExited: false }; }
    }

    /// <summary>Auflösung des Backend-Startpunkts: PyInstaller-Binary oder venv-Python (Dev).</summary>
    private static (string Exe, string? CliPy) ResolveBackendCommand()
    {
        var baseDir = AppContext.BaseDirectory;

        var exe = Path.Combine(baseDir, "backend", "audioscribe-backend.exe");
        if (File.Exists(exe))
            return (exe, null);

        var python = FindRepoVenvPython(baseDir);
        var cliPy = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "AudioScribe.Backend", "cli.py"));
        if (!File.Exists(cliPy))
            cliPy = Path.Combine(baseDir, "backend", "cli.py");

        if (python is not null && File.Exists(cliPy))
            return (python, cliPy);

        throw new InvalidOperationException(
            "Backend nicht gefunden. Entweder build/build_backend.bat ausführen (audioscribe-backend.exe) " +
            "oder ein venv unter src/AudioScribe.Backend/.venv anlegen (siehe README).");
    }

    private static string? FindRepoVenvPython(string baseDir)
    {
        var candidates = new[]
        {
            Path.Combine(baseDir, "..", "..", "..", "..", "AudioScribe.Backend", ".venv", "Scripts", "python.exe"),
            Path.Combine(baseDir, "backend", ".venv", "Scripts", "python.exe"),
        };
        foreach (var c in candidates)
        {
            var full = Path.GetFullPath(c);
            if (File.Exists(full)) return full;
        }
        return null;
    }

    /// <summary>Zentrales Modellverzeichnis: portable neben der EXE, sonst ProgramData\AudioScribe\models.</summary>
    private static string ResolveModelsDir()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var portableMarker = Path.Combine(baseDir, "portable.txt");
        var localFallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AudioScribe", "models");

        string modelsDir;
        try
        {
            modelsDir = File.Exists(portableMarker)
                ? Path.Combine(baseDir, "models")
                : Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AudioScribe", "models");
        }
        catch (Exception)
        {
            modelsDir = localFallback;
        }

        try
        {
            Directory.CreateDirectory(modelsDir);
            Directory.CreateDirectory(Path.Combine(modelsDir, "hub"));
            Directory.CreateDirectory(Path.Combine(modelsDir, "nltk_data"));
        }
        catch (UnauthorizedAccessException)
        {
            modelsDir = localFallback;
            Directory.CreateDirectory(modelsDir);
            Directory.CreateDirectory(Path.Combine(modelsDir, "hub"));
            Directory.CreateDirectory(Path.Combine(modelsDir, "nltk_data"));
        }

        return Path.GetFullPath(modelsDir);
    }

    public async Task RunTranscriptionAsync(string audioFilePath, string sessionDir, string hfToken, string model,
        int? minSpeakers = null, int? maxSpeakers = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(sessionDir);
        var (exe, cliPy) = ResolveBackendCommand();

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(exe)),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            // UTF-8 zwingend, sonst Umlaute-Crash beim JSON-Parsing
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };
        if (cliPy is not null)
            psi.ArgumentList.Add(cliPy);
        psi.ArgumentList.Add("transcribe");
        foreach (var arg in new[]
        {
            "--input", audioFilePath,
            "--device", "auto", // Backend entscheidet via torch.cuda.is_available()
            "--model", model,
            "--out-dir", sessionDir,
        })
            psi.ArgumentList.Add(arg);
        if (!string.IsNullOrWhiteSpace(hfToken))
        {
            psi.ArgumentList.Add("--hf-token");
            psi.ArgumentList.Add(hfToken);
        }
        if (minSpeakers is int mn)
        {
            psi.ArgumentList.Add("--min-speakers");
            psi.ArgumentList.Add(mn.ToString());
        }
        if (maxSpeakers is int mx)
        {
            psi.ArgumentList.Add("--max-speakers");
            psi.ArgumentList.Add(mx.ToString());
        }

        var modelsDir = ResolveModelsDir();
        psi.Environment["HF_HOME"] = modelsDir;
        psi.Environment["TORCH_HOME"] = modelsDir;
        psi.Environment["HUGGINGFACE_HUB_CACHE"] = Path.Combine(modelsDir, "hub");
        psi.Environment["HF_HUB_CACHE"] = Path.Combine(modelsDir, "hub");
        psi.Environment["NLTK_DATA"] = Path.Combine(modelsDir, "nltk_data");
        psi.Environment["AUDIOSCRIBE_MODEL_DIR"] = modelsDir;

        var pathParts = new List<string>
        {
            _hardware.EngineDirectory,
            Path.Combine(_hardware.EngineDirectory, "cuda"),
            Environment.GetEnvironmentVariable("PATH") ?? string.Empty,
        };
        psi.Environment["PATH"] = string.Join(Path.PathSeparator,
            pathParts.Where(p => !string.IsNullOrWhiteSpace(p)));

        psi.Environment["PYTHONUTF8"] = "1";
        psi.Environment["PYTHONIOENCODING"] = "utf-8";

        var logPath = Path.Combine(sessionDir, "backend.log");

        var process = new Process { StartInfo = psi };
        process.Start();
        lock (_gate) _process = process;

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            try { File.AppendAllText(logPath, e.Data + Environment.NewLine); }
            catch (IOException) { }
        };
        var lineHandler = new DataReceivedEventHandler((_ , e) =>
        {
            if (e.Data is null) return;
            try { DispatchJson(e.Data, sessionDir); }
            catch (JsonException) { Info?.Invoke("backend> " + e.Data); }
        });
        process.OutputDataReceived += lineHandler;

        process.BeginErrorReadLine();
        process.BeginOutputReadLine();

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            KillTree(process);
            throw;
        }
        finally
        {
            process.OutputDataReceived -= lineHandler;
            lock (_gate) _process = null;
        }

        if (process.ExitCode != 0 && !ReportedFailure)
        {
            Failed?.Invoke(
                $"Backend wurde mit Code {process.ExitCode} beendet. Log: {logPath}",
                "process_exit");
        }
        ReportedFailure = false;
    }

    private bool ReportedFailure { get; set; }

    /// <summary>JSON-Ereignisse in typisierte Events übersetzen (Dispatcher-Mapping in Phase C).</summary>
    private void DispatchJson(string line, string sessionDir)
    {
        line = line.Trim();
        if (line.Length == 0 || line[0] != '{')
        {
            Info?.Invoke("backend> " + line);
            return;
        }

        var evt = JsonSerializer.Deserialize<BackendEvent>(line, BackendJson.Options);
        if (evt is null) return;

        switch (evt.Type)
        {
            case "progress":
                ProgressChanged?.Invoke(new ProcessProgress
                {
                    Phase = ProcessProgress.ParsePhase(evt.Phase),
                    Percent = evt.Percent ?? 0,
                    StatusMessage = evt.Message ?? string.Empty,
                });
                break;
            case "info":
                Info?.Invoke(evt.Message ?? evt.Device ?? string.Empty);
                break;
            case "completed":
                if (evt.Data is { } data)
                {
                    var result = data.Deserialize<BackendResult>(BackendJson.Options);
                    if (result is not null)
                    {
                        result.SessionDir = sessionDir;
                        Completed?.Invoke(result);
                    }
                }
                break;
            case "error":
                ReportedFailure = true;
                Failed?.Invoke(evt.Message ?? "Unbekannter Backend-Fehler", evt.Code);
                break;
        }
    }

    public void Cancel()
    {
        lock (_gate)
        {
            if (_process is { HasExited: false } p)
                KillTree(p);
        }
    }

    /// <summary>Prozess samt Kindprozessen (torch/CUDA) beenden, damit VRAM freigegeben wird.</summary>
    private static void KillTree(Process p)
    {
        try
        {
            p.Kill(entireProcessTree: true);
            p.WaitForExit(5000);
        }
        catch (InvalidOperationException) { /* bereits beendet */ }
    }
}
