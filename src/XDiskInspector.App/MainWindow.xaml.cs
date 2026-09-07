using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace XDiskInspector.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }

    private void MainTabs_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, sender) || !SystemParameters.ClientAreaAnimation) return;
        if (sender is not TabControl tabs || tabs.SelectedContent is not FrameworkElement content) return;

        content.BeginAnimation(OpacityProperty, new DoubleAnimation
        {
            From = 0.25,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(160),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        });
    }
}
