using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AudioScribe.App.Services;
using AudioScribe.App.ViewModels;
using Microsoft.Win32;

namespace AudioScribe.App.Views;

public partial class DropZoneView : UserControl
{
    private DropZoneViewModel Vm => (DropZoneViewModel)DataContext;

    public DropZoneView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is DropZoneViewModel vm)
                vm.OpenFilePickerRequested += ShowOpenDialog;
        };
        DragEnter += OnDragEnter;
        DragLeave += OnDragLeave;
        Drop += OnDrop;
    }

    private static readonly string[] Allowed = DropZoneViewModel.AllowedExtensions.Split(',');

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (HasAudioFile(e))
        {
            e.Effects = DragDropEffects.Copy;
            DropBorder.BorderThickness = new Thickness(3);
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        DropBorder.BorderThickness = new Thickness(2);
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        DropBorder.BorderThickness = new Thickness(2);
        e.Handled = true;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            SetFileWithProbe(files[0]);
    }

    private void SetFileWithProbe(string path)
    {
        Vm.SetFile(path);
        if (Vm.FilePath == path)
        {
            AudioDurationProbe.Probe(path, Dispatcher, text =>
            {
                if (Vm.FilePath == path)
                    Vm.DurationText = string.IsNullOrEmpty(text) ? string.Empty : $" · Dauer {text}";
            });
        }
    }

    private static bool HasAudioFile(DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) &&
            e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
        {
            var ext = Path.GetExtension(files[0]).ToLowerInvariant();
            return Allowed.Contains(ext);
        }
        return false;
    }

    private void OnPickFileClick(object sender, MouseButtonEventArgs e) => Vm.RequestOpenPicker();

    private void ShowOpenDialog(string filterExtensions)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Audiodatei wählen",
            Filter = "Audiodateien|*." + string.Join(";*.", filterExtensions.Split(',')) + "|Alle Dateien|*.*",
        };
        if (dlg.ShowDialog(Window.GetWindow(this)) == true)
            SetFileWithProbe(dlg.FileName);
    }

    private void OnStartClick(object sender, RoutedEventArgs e) => Vm.NotifyStart();

    private void OnOpenTermsClick(object sender, RoutedEventArgs e) =>
        OpenInBrowser("https://huggingface.co/pyannote/speaker-diarization-community-1");

    private void OnOpenTokenClick(object sender, RoutedEventArgs e) =>
        OpenInBrowser("https://huggingface.co/settings/tokens?type=read");

    private void OnDownloadGpuAddonClick(object sender, RoutedEventArgs e) => Vm.DownloadGpuAddon();

    private void OnEditTokenClick(object sender, RoutedEventArgs e) => Vm.BeginEditToken();
    private void OnRemoveTokenClick(object sender, RoutedEventArgs e) => Vm.ClearStoredToken();
    private void OnHelpToggleClick(object sender, RoutedEventArgs e) => Vm.ToggleHelp();

    private static void OpenInBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Die Seite konnte nicht automatisch geöffnet werden.\n\n{url}\n\n{ex.Message}",
                "Browser-Link öffnen fehlgeschlagen",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
