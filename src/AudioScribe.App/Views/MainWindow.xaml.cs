using System.ComponentModel;
using System.Windows;
using AudioScribe.App.Services;
using AudioScribe.App.ViewModels;

namespace AudioScribe.App.Views;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>Hart beenden statt verstecken: Backend-Prozessbaum killen, sonst bleibt CUDA/VRAM belegt.</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (DataContext is MainViewModel)
        {
            (Application.Current as App)?.ShutdownBackendIfRunning();
        }
    }
}
