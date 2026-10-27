using System.Windows.Threading;
using System.Windows.Media;

namespace AudioScribe.App.Services;

/// <summary>
/// MediaPlayer-basierte Wiedergabe der Speaker-Previews.
/// WICHTIG: MediaPlayer sperrt Quelldateien; daher wird bei Pause/Stop/Reload immer
/// Close() bzw. Open(null) aufgerufen, damit Session-Temp-Ordner gelöscht werden können.
/// </summary>
public sealed class AudioPlayerService : IAudioPlayerService
{
    private readonly Dispatcher _dispatcher;
    private MediaPlayer? _player;
    private string? _currentPath;
    private TimeSpan? _pausedPosition;
    private bool _paused;
    private bool _isPlaying;

    public event Action<bool>? PlaybackStateChanged;

    public AudioPlayerService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public bool IsPlaying => _isPlaying && !_paused;
    public bool IsOpen => _currentPath is not null;
    public double PositionSeconds => _player?.Position.TotalSeconds ?? 0;

    public void Play(string filePath)
    {
        _dispatcher.Invoke(() =>
        {
            ReleasePlayer(); // alte Datei-Sperre aufheben, bevor neu geöffnet wird
            _player = new MediaPlayer();
            _player.MediaEnded += OnMediaEnded;
            _player.MediaFailed += OnMediaFailed;
            _currentPath = filePath;
            _paused = false;
            _isPlaying = true;
            _player.Open(new Uri(filePath, UriKind.Absolute));
            _player.Play();
            RaiseState();
        });
    }

    public void Pause()
    {
        _dispatcher.Invoke(() =>
        {
            if (_player is null || _paused) return;
            _pausedPosition = _player.Position;
            _player.Pause();
            _paused = true;
            _isPlaying = false;
            // Sperre trotzdem freigeben: Stopping hebt Load auf, Position wird zwischengespeichert.
            _player.Stop();
            RaiseState();
        });
    }

    public void Resume()
    {
        _dispatcher.Invoke(() =>
        {
            if (_player is null || _currentPath is null) return;
            var pos = _player.Position > TimeSpan.Zero ? _player.Position : (_pausedPosition ?? TimeSpan.Zero);
            _player.Open(new Uri(_currentPath, UriKind.Absolute));
            _player.Position = pos;
            _player.Play();
            _paused = false;
            _isPlaying = true;
            RaiseState();
        });
    }

    public void Stop()
    {
        _dispatcher.Invoke(() =>
        {
            ReleasePlayer();
            RaiseState();
        });
    }

    public void Seek(double seconds)
    {
        _dispatcher.Invoke(() =>
        {
            if (_player is null || _currentPath is null) return;
            _player.Position = TimeSpan.FromSeconds(Math.Max(0, seconds));
        });
    }

    private void OnMediaEnded(object? _, EventArgs __)
    {
        _dispatcher.Invoke(() =>
        {
            ReleasePlayer();
            RaiseState();
        });
    }

    private void OnMediaFailed(object? sender, ExceptionEventArgs e)
    {
        _dispatcher.Invoke(() =>
        {
            ReleasePlayer();
            RaiseState();
        });
    }

    /// <summary>Entfernt MediaOpened-Event hooks und gibt die Dateisperre vollständig frei.</summary>
    private void ReleasePlayer()
    {
        if (_player is null) return;
        _player.MediaEnded -= OnMediaEnded;
        _player.MediaFailed -= OnMediaFailed;
        _player.Close();
        _player = null;
        _currentPath = null;
        _paused = false;
        _isPlaying = false;
    }

    private void RaiseState() => PlaybackStateChanged?.Invoke(IsPlaying);

    public void Dispose()
    {
        try
        {
            _dispatcher.Invoke(ReleasePlayer);
        }
        catch (Exception ex) when (ex is InvalidOperationException)
        {
            ReleasePlayer();
        }
    }
}
