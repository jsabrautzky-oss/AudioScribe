using System.Windows;
using System.Windows.Controls;
using AudioScribe.App.Models;
using AudioScribe.App.ViewModels;

namespace AudioScribe.App.Views;

public partial class SpeakerReviewView : UserControl
{
    private SpeakerReviewViewModel Vm => (SpeakerReviewViewModel)DataContext;

    public SpeakerReviewView()
    {
        InitializeComponent();
    }

    private void OnPlayClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SpeakerModel speaker })
            Vm.TogglePlay(speaker);
    }

    private void OnStopClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SpeakerModel speaker })
            Vm.TogglePlay(speaker);
    }

    private void OnApplyClick(object sender, RoutedEventArgs e) => Vm.NotifyApplyNames();
    private void OnBackClick(object sender, RoutedEventArgs e) => Vm.NotifyBack();
}
