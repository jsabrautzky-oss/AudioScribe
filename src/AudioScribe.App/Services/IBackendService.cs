using AudioScribe.App.Models;

namespace AudioScribe.App.Services;

/// <summary>Startet und überwacht das Python-Backend als Subprozess (JSON-Streaming über STDOUT).</summary>
public interface IBackendService
{
    /// <summary>Phasen-Fortschritt, threadsicher auf den UI-Dispatcher gemappt.</summary>
    event Action<ProcessProgress>? ProgressChanged;

    /// <summary>Verarbeitung fertig; Ergebnis des "completed"-Events.</summary>
    event Action<BackendResult>? Completed;

    /// <summary>Fehlertext inkl. optionalem maschinenlesbaren Code (z. B. "hf_auth").</summary>
    event Action<string, string?>? Failed;

    /// <summary>Infozeile, z. B. erkanntes Device vom Backend ("cuda fp16" / "cpu int8").</summary>
    event Action<string>? Info;

    /// <summary>
    /// Startet die Pipeline. device immer "auto" — die Backend-Seite entscheidet via
    /// torch.cuda.is_available(). Gibt Task zurück, das endet, wenn der Prozess terminiert.
    /// </summary>
    Task RunTranscriptionAsync(string audioFilePath, string sessionDir, string hfToken, string model,
        int? minSpeakers = null, int? maxSpeakers = null, CancellationToken ct = default);

    /// <summary>Hart abbrechen inkl. gesamtem Prozessbaum (Kill(entireProcessTree: true)), gibt VRAM frei.</summary>
    void Cancel();

    /// <summary>True, solange ein Backend-Prozess läuft.</summary>
    bool IsRunning { get; }
}
