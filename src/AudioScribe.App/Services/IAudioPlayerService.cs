namespace AudioScribe.App.Services;

/// <summary>Wiedergabe lokaler Audiodateien (Speaker-Previews). Gibt Dateisperren aktiv frei.</summary>
public interface IAudioPlayerService : IDisposable
{
    event Action<bool>? PlaybackStateChanged;

    bool IsPlaying { get; }
    bool IsOpen { get; }

    /// <summary>Lädt und startet Wiedergabe; sperrt die Datei nur, bis Stop/Close aufgerufen wird.</summary>
    void Play(string filePath);

    void Pause();
    void Resume();
    void Stop();

    /// <summary>Position in Sekunden (0 wenn nichts geladen).</summary>
    double PositionSeconds { get; }

    void Seek(double seconds);
}
