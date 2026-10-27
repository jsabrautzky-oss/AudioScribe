using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using AudioScribe.App.Models;
using AudioScribe.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AudioScribe.App.ViewModels;

/// <summary>Transkript-Viewer (virtualisiert) + Export: Markdown, TXT, SRT, JSON, Clipboard.</summary>
public partial class TranscriptResultViewModel : ObservableObject
{
    public ObservableCollection<TranscriptSegment> Segments { get; } = [];
    public ObservableCollection<SpeakerModel> Speakers { get; } = [];

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = string.Empty;

    // Metadaten für Frontmatter/JSON
    public string Title { get; set; } = "Transkript";
    public string SourceFile { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public double DurationSeconds { get; set; }
    public string Device { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;

    /// <summary>(format, vorgeschlagener Dateiname) — Code-Behind öffnet den SaveFileDialog.</summary>
    public event Action<ExportFormat, string>? SaveRequested;

    public event Action? BackRequested;

    public void NotifyBack() => BackRequested?.Invoke();

    private readonly IExportService _export;

    public TranscriptResultViewModel(IExportService export) => _export = export;

    public ExportBundle BuildBundle() => new(
        Segments.ToList(),
        Speakers.ToList(),
        new TranscriptMetadata(Title, SourceFile, CreatedAt, DurationSeconds, Device, Model));

    [RelayCommand]
    public void CopyForLlm()
    {
        _export.CopyMarkdownToClipboard(BuildBundle());
        StatusText = "Markdown in die Zwischenablage kopiert — bereit für den LLM-Agenten.";
    }

    [RelayCommand]
    public void SaveMarkdown() => SaveRequested?.Invoke(ExportFormat.Markdown, Title + ".md");

    [RelayCommand]
    public void SaveText() => SaveRequested?.Invoke(ExportFormat.Text, Title + ".txt");

    [RelayCommand]
    public void SaveSubRip() => SaveRequested?.Invoke(ExportFormat.SubRip, Title + ".srt");

    [RelayCommand]
    public void SaveJson() => SaveRequested?.Invoke(ExportFormat.Json, Title + ".json");

    /// <summary>Hilfsaufruf aus dem Picker-Handler nach Dateipfad-Wahl.</summary>
    public string FinishSave(ExportFormat format, string path)
    {
        var written = _export.SaveToFile(format, path, BuildBundle());
        StatusText = $"Exportiert: {Path.GetFileName(written)}";
        return written;
    }

    public void Reset()
    {
        Segments.Clear();
        Speakers.Clear();
        StatusText = string.Empty;
        Title = "Transkript";
        SourceFile = string.Empty;
        DurationSeconds = 0;
        Device = string.Empty;
        Model = string.Empty;
    }
}
