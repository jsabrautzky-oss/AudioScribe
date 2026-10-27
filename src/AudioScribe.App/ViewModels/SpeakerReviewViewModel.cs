using System.Collections.ObjectModel;
using AudioScribe.App.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AudioScribe.App.ViewModels;

/// <summary>Sprecher-Hörproben & Namensvergabe (Live-Update der Segment-Anzeigen).</summary>
public partial class SpeakerReviewViewModel : ObservableObject
{
    public ObservableCollection<SpeakerModel> Speakers { get; } = [];

    [ObservableProperty]
    private string _playingSpeakerId = string.Empty;

    public event Action<SpeakerModel>? PlayToggled;
    public event Action? ApplyNamesRequested;
    public event Action? BackRequested;

    public void NotifyApplyNames() => ApplyNamesRequested?.Invoke();
    public void NotifyBack() => BackRequested?.Invoke();

    public void TogglePlay(SpeakerModel speaker) => PlayToggled?.Invoke(speaker);

    public void Clear()
    {
        foreach (var s in Speakers) s.OnRenamed = null;
        Speakers.Clear();
    }
}
