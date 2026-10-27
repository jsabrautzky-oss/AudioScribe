using AudioScribe.App.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AudioScribe.App.ViewModels;

/// <summary>Phasen-Fortschritt gemäß Backend-Protokoll (audio → transcription/alignment → diarization → preparing).</summary>
public partial class ProgressViewModel : ObservableObject
{
    [ObservableProperty]
    private ProcessProgress _current = new() { Phase = ProcessingPhase.None };

    [ObservableProperty]
    private double _overallPercent;

    [ObservableProperty]
    private string _phaseMessage = "Warte auf Backend…";

    [ObservableProperty]
    private string _infoText = string.Empty;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _errorText = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    /// <summary>0-basiertes Stepper-Ziel: 0..3 = vier Phasen, 4 = fertig.</summary>
    public int PhaseIndex => Current.Phase switch
    {
        ProcessingPhase.AudioAnalysis => 0,
        ProcessingPhase.Transcription => 1,
        ProcessingPhase.Diarization => 2,
        ProcessingPhase.Preparing => 3,
        ProcessingPhase.Completed => 4,
        _ => -1,
    };

    partial void OnCurrentChanged(ProcessProgress value)
    {
        OnPropertyChanged(nameof(PhaseIndex));
        PhaseMessage = value.StatusMessage;
    }

    public event Action? CancelRequested;
    public event Action? BackRequested;

    public void NotifyCancel() => CancelRequested?.Invoke();
    public void NotifyBack() => BackRequested?.Invoke();

    public void BeginRun()
    {
        ErrorText = string.Empty;
        HasError = false;
        InfoText = string.Empty;
        OverallPercent = 0;
        Current = new ProcessProgress { Phase = ProcessingPhase.AudioAnalysis };
        IsRunning = true;
    }

    public void Apply(ProcessProgress progress)
    {
        Current = progress;
        OverallPercent = progress.Phase switch
        {
            ProcessingPhase.AudioAnalysis => progress.Percent * 0.10,
            ProcessingPhase.Transcription => 10 + progress.Percent * 0.55,
            ProcessingPhase.Diarization => 65 + progress.Percent * 0.25,
            ProcessingPhase.Preparing => 90 + progress.Percent * 0.10,
            ProcessingPhase.Completed => 100,
            _ => OverallPercent,
        };
    }

    public void EndRun() => IsRunning = false;

    public void Fail(string message, string? code)
    {
        string hint = code == "hf_auth"
            ? "\nTipp: Modellbedingungen auf huggingface.co/pyannote/speaker-diarization-community-1 akzeptieren und Token prüfen."
            : string.Empty;
        ErrorText = message + hint;
        HasError = true;
    }
}
