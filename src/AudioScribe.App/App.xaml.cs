using System.IO;
using System.Windows;
using AudioScribe.App.Services;
using AudioScribe.App.ViewModels;
using AudioScribe.App.Views;
using Microsoft.Extensions.DependencyInjection;

namespace AudioScribe.App;

public partial class App : Application
{
    private static ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            try
            {
                var log = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AudioScribe", "error.log");
                Directory.CreateDirectory(Path.GetDirectoryName(log)!);
                File.AppendAllText(log, $"[{DateTime.Now:O}] {args.Exception}\n\n");
            }
            catch { /* nur Best Effort */ }
        };

        var services = new ServiceCollection();
        services.AddSingleton<IAppConfigStore, AppDataConfigStore>();
        services.AddSingleton<HardwareDetectorService>();
        services.AddSingleton<IGpuAddonService, GpuAddonService>();
        services.AddSingleton<IBackendService, BackendRunnerService>();
        services.AddSingleton<IAudioPlayerService>(_ => new AudioPlayerService(Dispatcher));
        services.AddSingleton<IExportService, ExportService>();
        services.AddSingleton<DropZoneViewModel>();
        services.AddSingleton<ProgressViewModel>();
        services.AddSingleton<SpeakerReviewViewModel>();
        services.AddSingleton<TranscriptResultViewModel>();
        services.AddSingleton<MainViewModel>();
        _services = services.BuildServiceProvider();

        Exit += OnAppExit;

        var window = new MainWindow
        {
            DataContext = _services.GetRequiredService<MainViewModel>(),
        };
        MainWindow = window;
        window.Show();
    }

    /// <summary>Beim App-Exit Backend-Baum killen, damit keine Python/CUDA-Prozesse VRAM blockieren.</summary>
    private void OnAppExit(object? sender, ExitEventArgs e)
    {
        try
        {
            _services?.GetService<IBackendService>()?.Cancel();
            _services?.GetService<IAudioPlayerService>()?.Dispose();
        }
        catch { /* beim Shutdown stillschweigend */ }
        _services?.Dispose();
    }

    /// <summary>Von MainWindow.OnClosing gerufen: laufendes Backend vor dem Fenster-Schließen beenden.</summary>
    public void ShutdownBackendIfRunning()
    {
        try { _services?.GetService<IBackendService>()?.Cancel(); }
        catch { /* Prozess bereits beendet */ }
    }
}
