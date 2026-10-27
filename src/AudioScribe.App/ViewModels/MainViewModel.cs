using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using AudioScribe.App.Models;
using AudioScribe.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AudioScribe.App.ViewModels;

public enum AppStep { DropZone, Progress, SpeakerReview, TranscriptResult }

/// <summary>
/// Orchestriert DropZone -> Progress -> SpeakerReview -> TranscriptResult,
/// übersetzt Backend-Events (ThreadPool!) threadsicher in UI-Zustand.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly IBackendService _backend;
    private readonly IAppConfigStore _config;
    private readonly IAudioPlayerService _player;
    private readonly Dispatcher _dispatcher;

    private string _sessionDir = string.Empty;
    private readonly List<SpeakerPlayback> _playbacks = [];
    private SpeakerPlayback? _playing;

    public DropZoneViewModel DropZone { get; }
    public ProgressViewModel Progress { get; }
    public SpeakerReviewViewModel SpeakerReview { get; }
    public TranscriptResultViewModel TranscriptResult { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StepTitle))]
    private ObservableObject _currentStep;

    [ObservableProperty]
    private string _statusBarText = "Bereit";

    [ObservableProperty]
    private AppStep _step = AppStep.DropZone;

    public string StepTitle => Step switch
    {
        AppStep.DropZone => "Datei auswählen",
        AppStep.Progress => "Verarbeitung",
        AppStep.SpeakerReview => "Sprecher identifizieren",
        AppStep.TranscriptResult => "Transkript & Export",
        _ => string.Empty,
    };

    public MainViewModel(
        IBackendService backend,
        IAppConfigStore config,
        IAudioPlayerService player,
        DropZoneViewModel dropZone,
        ProgressViewModel progress,
        SpeakerReviewViewModel speakerReview,
        TranscriptResultViewModel transcriptResult)
    {
        _backend = backend;
        _config = config;
        _player = player;
        _dispatcher = Dispatcher.CurrentDispatcher;

        DropZone = dropZone;
        Progress = progress;
        SpeakerReview = speakerReview;
        TranscriptResult = transcriptResult;
        _currentStep = dropZone;

        DropZone.StartRequested += StartRun;
        SpeakerReview.PlayToggled += TogglePlayback;
        SpeakerReview.ApplyNamesRequested += ApplyNamesAndShowTranscript;
        Progress.CancelRequested += () => { _backend.Cancel(); };
        Progress.BackRequested += ResetToStart;
        SpeakerReview.BackRequested += ResetToStart;
        TranscriptResult.BackRequested += ResetToStart;

        _backend.ProgressChanged += p => _dispatcher.Invoke(() => Progress.Apply(p));
        _backend.Info += msg => _dispatcher.Invoke(() => Progress.InfoText = msg);
        _backend.Completed += result => _dispatcher.Invoke(() => OnCompleted(result));
        _backend.Failed += (msg, code) => _dispatcher.Invoke(() =>
        {
            Progress.EndRun();
            Progress.Fail(msg, code);
            StatusBarText = "Fehler bei der Verarbeitung";
        });
    }

    partial void OnStepChanged(AppStep value)
    {
        CurrentStep = value switch
        {
            AppStep.DropZone => (ObservableObject)DropZone,
            AppStep.Progress => Progress,
            AppStep.SpeakerReview => SpeakerReview,
            AppStep.TranscriptResult => TranscriptResult,
            _ => CurrentStep,
        };
        StatusBarText = value switch
        {
            AppStep.Progress => "Verarbeitung läuft…",
            AppStep.SpeakerReview => "Sprecher identifizieren",
            AppStep.TranscriptResult => "Transkript bereit",
            _ => "Bereit",
        };
    }

    // ---------------------------------------------------------------- Start

    private void StartRun()
    {
        if (!DropZone.CanStart) return;
        var (mn, mx, boundsError) = DropZone.ReadSpeakerBounds();
        if (boundsError.Length > 0)
        {
            DropZone.ValidationMessage = boundsError;
            return;
        }

        if (DropZone.SaveToken)
            _config.SaveHfToken(DropZone.HfToken);
        _config.SetModel(DropZone.SelectedModel);
        var token = DropZone.SaveToken && !string.IsNullOrWhiteSpace(DropZone.HfToken)
            ? DropZone.HfToken
            : _config.GetHfToken();

        var safeStem = Path.GetFileNameWithoutExtension(DropZone.FilePath ?? string.Empty);
        safeStem = string.Concat(safeStem.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        _sessionDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AudioScribe", "sessions", $"{DateTime.Now:yyyy-MM-dd_HHmmss}_{safeStem}");

        Progress.BeginRun();
        Step = AppStep.Progress;

        _ = RunAsync(DropZone.FilePath!, token ?? string.Empty, DropZone.SelectedModel, mn, mx);
    }

    private async Task RunAsync(string file, string token, string model, int? mn, int? mx)
    {
        try
        {
            await _backend.RunTranscriptionAsync(file, _sessionDir, token, model, mn, mx);
        }
        catch (Exception ex)
        {
            _dispatcher.Invoke(() =>
            {
                Progress.EndRun();
                Progress.Fail($"Backend konnte nicht gestartet werden: {ex.Message}", "start_failed");
            });
        }
    }

    // ---------------------------------------------------------------- Abschluss / Mapping

    /// <summary>Verkabelt Rename-Callback + Playback-Tracking (wird pro Sitzung verdrahtet).</summary>
    public void WireSpeaker(SpeakerModel model)
    {
        model.OnRenamed = _ => RefreshSegmentNames();
        var pb = new SpeakerPlayback(model);
        _playbacks.Add(pb);
        pb.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SpeakerPlayback.IsPlaying))
                SpeakerReview.PlayingSpeakerId = pb.IsPlaying ? pb.Owner.Id : string.Empty;
        };
    }

    private void OnCompleted(BackendResult result)
    {
        _player.Stop();
        Progress.EndRun();

        TranscriptResult.Reset();
        SpeakerReview.Clear();
        _playbacks.Clear();
        _playing = null;

        TranscriptResult.Title = Path.GetFileNameWithoutExtension(DropZone.FilePath ?? "Transkript");
        TranscriptResult.SourceFile = DropZone.FilePath ?? string.Empty;
        TranscriptResult.CreatedAt = DateTimeOffset.Now;
        TranscriptResult.DurationSeconds = result.DurationSeconds;
        TranscriptResult.Device = result.Device;
        TranscriptResult.Model = result.Model;

        foreach (var s in result.Segments)
        {
            TranscriptResult.Segments.Add(new TranscriptSegment
            {
                SegmentId = s.Id,
                SpeakerId = s.SpeakerId,
                Start = s.Start,
                End = s.End,
                Text = s.Text,
                AverageConfidence = s.AvgConfidence,
                Words = s.Words.Select(w => new WordToken
                {
                    Start = w.Start,
                    End = w.End,
                    Word = w.Word,
                    Score = w.Score,
                }).ToList(),
            });
        }

        int n = 0;
        foreach (var sp in result.Speakers)
        {
            n++;
            var model = new SpeakerModel
            {
                Id = sp.Id,
                PreviewPath = string.IsNullOrEmpty(sp.PreviewFile)
                    ? string.Empty
                    : Path.Combine(result.SessionDir, sp.PreviewFile),
                PreviewSnippet = sp.PreviewSnippet ?? string.Empty,
                TotalTimeSeconds = sp.TotalTime,
                PercentageOfTotal = sp.Percent,
                FallbackDisplayName = $"Sprecher {n}",
            };
            SpeakerReview.Speakers.Add(model);
            WireSpeaker(model);
        }

        // Vorbelegung: Fallback-Namen sichtbar, Segmente direkt beschriften (Live-Effekt ab Tippen)
        RefreshSegmentNames();

        Progress.InfoText = $"Session: {_sessionDir}";
        Step = AppStep.SpeakerReview;
    }

    // ---------------------------------------------------------------- Wiedergabe

    private void TogglePlayback(SpeakerModel speaker)
    {
        var pb = _playbacks.FirstOrDefault(p => p.Owner == speaker);
        if (pb is null || string.IsNullOrEmpty(speaker.PreviewPath)) return;

        if (_playing == pb)
        {
            _player.Stop();
            pb.IsPlaying = false;
            _playing = null;
            return;
        }

        if (_playing is not null)
        {
            _player.Stop();
            _playing.IsPlaying = false;
        }
        _player.Play(speaker.PreviewPath);
        pb.IsPlaying = true;
        _playing = pb;
    }

    // ---------------------------------------------------------------- Namen & Abschluss

    private void RefreshSegmentNames()
    {
        var map = SpeakerReview.Speakers.ToDictionary(s => s.Id, s => s.ResolvedName);
        foreach (var seg in TranscriptResult.Segments)
        {
            if (map.TryGetValue(seg.SpeakerId, out var name))
                seg.DisplaySpeakerName = name;
            seg.RefreshName();
        }
    }

    [RelayCommand]
    public void ApplyNamesAndShowTranscript()
    {
        _player.Stop();
        if (_playing is not null) { _playing.IsPlaying = false; _playing = null; }
        RefreshSegmentNames();
        Step = AppStep.TranscriptResult;
    }

    [RelayCommand]
    public void ResetToStart()
    {
        _backend.Cancel();
        _player.Stop();
        if (_playing is not null) { _playing.IsPlaying = false; _playing = null; }
        Progress.EndRun();
        Step = AppStep.DropZone;
    }
}
