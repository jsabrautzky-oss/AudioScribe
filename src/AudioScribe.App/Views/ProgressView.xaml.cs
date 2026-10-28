using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AudioScribe.App.ViewModels;

namespace AudioScribe.App.Views;

public partial class ProgressView : UserControl
{
    private static readonly string[] StepNames = ["Step0", "Step1", "Step2", "Step3"];
    private Border[] _steps = [];

    private ProgressViewModel? Vm => DataContext as ProgressViewModel;
    private ProgressViewModel? _subscribedVm;

    public ProgressView()
    {
        InitializeComponent();
        _steps = new[]
        {
            (Border)FindName("Step0")!, (Border)FindName("Step1")!,
            (Border)FindName("Step2")!, (Border)FindName("Step3")!,
        };
        DataContextChanged += (_, _) =>
        {
            if (_subscribedVm != null)
                _subscribedVm.PropertyChanged -= OnVmChanged;
            _subscribedVm = DataContext as ProgressViewModel;
            if (_subscribedVm != null)
            {
                _subscribedVm.PropertyChanged += OnVmChanged;
                UpdateVisual(_subscribedVm);
            }
            else
            {
                UpdateVisual(null);
            }
        };
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProgressViewModel.PhaseIndex)
            or nameof(ProgressViewModel.IsRunning)
            or nameof(ProgressViewModel.HasError))
        {
            Dispatcher.Invoke(() => UpdateVisual(sender as ProgressViewModel));
        }
    }

    private void UpdateVisual(ProgressViewModel? vm)
    {
        if (vm == null || vm.Current == null)
            return;

        var accent = TryFindResource("AccentFillColorDefaultBrush") as Brush
                     ?? Brushes.DodgerBlue;
        var card = TryFindResource("CardBackgroundFillColorDefaultBrush") as Brush
                   ?? Brushes.Transparent;
        int phaseIndex = vm.PhaseIndex;
        for (int i = 0; i < _steps.Length; i++)
        {
            var step = _steps[i];
            if (step == null)
                continue;
            step.Background = i <= phaseIndex ? accent : card;
        }

        if (CancelButton != null)
            CancelButton.Visibility = vm.IsRunning ? Visibility.Visible : Visibility.Collapsed;
        if (BackButton != null)
            BackButton.Visibility = vm.IsRunning ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Vm?.NotifyCancel();
    private void OnBackClick(object sender, RoutedEventArgs e) => Vm?.NotifyBack();
}
