using System.Windows;
using System.Windows.Controls;
using AudioScribe.App.Services;
using AudioScribe.App.ViewModels;
using Microsoft.Win32;

namespace AudioScribe.App.Views;

public partial class TranscriptResultView : UserControl
{
    private TranscriptResultViewModel Vm => (TranscriptResultViewModel)DataContext;

    public TranscriptResultView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is TranscriptResultViewModel vm)
                vm.SaveRequested += ShowSaveDialog;
        };
    }

    private void ShowSaveDialog(ExportFormat format, string suggestedName)
    {
        var (filter, ext) = format switch
        {
            ExportFormat.Markdown => ("Markdown-Datei (*.md)|*.md", ".md"),
            ExportFormat.Text => ("Textdatei (*.txt)|*.txt", ".txt"),
            ExportFormat.SubRip => ("Untertitel (*.srt)|*.srt", ".srt"),
            ExportFormat.Json => ("JSON-Session (*.json)|*.json", ".json"),
            _ => ("Alle Dateien|*.*", string.Empty),
        };

        var dlg = new SaveFileDialog
        {
            Title = "Export speichern",
            Filter = filter,
            FileName = suggestedName,
            DefaultExt = ext,
            AddExtension = true,
        };
        if (dlg.ShowDialog(Window.GetWindow(this)) == true)
            Vm.FinishSave(format, dlg.FileName);
    }

    private void OnBackClick(object sender, RoutedEventArgs e) => Vm.NotifyBack();
}
