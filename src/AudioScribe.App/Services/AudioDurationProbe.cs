using System.Windows.Threading;
using System.Windows.Media;

namespace AudioScribe.App.Services;

/// <summary>
/// Dauer-Ermittlung über denselben MediaPlayer wie die Wiedergabe (MediaFoundation).
/// Asynchron; onDurationText feuert maximal einmal auf dem UI-Dispatcher.
/// Der Player wird danach geschlossen (keine Dateisperre). WAV schlägt bei MF oft
/// fehl -> leerer String.
/// </summary>
public static class AudioDurationProbe
{
    public static void Probe(string path, Dispatcher dispatcher, Action<string> onDurationText)
    {
        MediaPlayer? player = null;
        DispatcherTimer? timeout = null;
        bool finished = false;

        void Finish(string text)
        {
            if (finished) return;
            finished = true;
            timeout?.Stop();
            if (player is not null)
            {
                player.MediaOpened -= OnOpened;
                player.MediaFailed -= OnFailed;
                player.Close();
                player = null;
            }
            onDurationText(text);
        }

        void OnOpened(object? _, EventArgs __)
        {
            dispatcher.Invoke(() =>
            {
                var d = player?.NaturalDuration;
                string text = d.HasValue && d.Value.HasTimeSpan
                    ? FormatTimeSpan(d.Value.TimeSpan)
                    : string.Empty;
                Finish(text);
            });
        }

        void OnFailed(object? _, ExceptionEventArgs __) => dispatcher.Invoke(() => Finish(string.Empty));

        try
        {
            player = new MediaPlayer();
        }
        catch (Exception)
        {
            onDurationText(string.Empty);
            return;
        }

        timeout = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromSeconds(5),
        };
        timeout.Tick += (_, __) => Finish(string.Empty);

        player.MediaOpened += OnOpened;
        player.MediaFailed += OnFailed;
        player.Open(new Uri(path, UriKind.Absolute));
        timeout.Start();
    }

    private static string FormatTimeSpan(TimeSpan ts)
    {
        var h = (int)ts.TotalHours;
        return h > 0 ? $"{h}:{ts.Minutes:00}:{ts.Seconds:00}" : $"{ts.Minutes}:{ts.Seconds:00}";
    }
}
