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

    private ProgressViewModel Vm => (ProgressViewModel)DataContext;

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
            if (DataContext is ProgressViewModel vm)
            {
                vm.PropertyChanged += OnVmChanged;
                UpdateVisual(vm);
            }
        };
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProgressViewModel.PhaseIndex)
            or nameof(ProgressViewModel.IsRunning)
            or nameof(ProgressViewModel.HasError))
        {
            Dispatcher.Invoke(() => UpdateVisual(Vm));
        }
    }

    private void UpdateVisual(ProgressViewModel vm)
    {
        var accent = TryFindResource("AccentFillColorDefaultBrush") as Brush
                     ?? Brushes.DodgerBlue;
        var card = TryFindResource("CardBackgroundFillColorDefaultBrush") as Brush
                   ?? Brushes.Transparent;
        for (int i = 0; i < _steps.Length; i++)
            _steps[i].Background = i <= vm.PhaseIndex ? accent : card;

        CancelButton.Visibility = vm.IsRunning ? Visibility.Visible : Visibility.Collapsed;
        BackButton.Visibility = vm.IsRunning ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Vm.NotifyCancel();
    private void OnBackClick(object sender, RoutedEventArgs e) => Vm.NotifyBack();
}
