using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using XDiskInspector.Cleanup;
using XDiskInspector.Reporting;

namespace XDiskInspector.App;

public sealed class CleanupConfirmationWindow : Window
{
    private readonly CheckBox _irreversibleCheck;
    private readonly Button _confirmButton;

    public bool Confirmed { get; private set; }
    public bool AllowRecycleBinIrreversible => _irreversibleCheck.IsChecked == true;

    public CleanupConfirmationWindow(CleanupPreview preview)
    {
        Title = "最终清理确认";
        Width = 760;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        Background = (System.Windows.Media.Brush)Application.Current.Resources["BgBrush"];
        Foreground = (System.Windows.Media.Brush)Application.Current.Resources["TextBrush"];

        var root = new Grid { Margin = new Thickness(22) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock { Text = "将按以下最终清单顺序执行", FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) };
        root.Children.Add(title);

        var summary = new TextBlock
        {
            Text = $"共 {preview.Candidates.Count} 项 · 预计逻辑容量 {ByteFormatter.Format(preview.EstimatedBytes)}。执行前会再次校验路径、规则版本、时间与重解析点状态。",
            Foreground = (System.Windows.Media.Brush)Application.Current.Resources["MutedBrush"],
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        };
        Grid.SetRow(summary, 1); root.Children.Add(summary);

        var grid = new DataGrid { ItemsSource = preview.Candidates, Margin = new Thickness(0, 0, 0, 12), IsReadOnly = true, AutoGenerateColumns = false };
        grid.Columns.Add(new DataGridTextColumn { Header = "路径", Binding = new Binding(nameof(CleanupCandidate.Path)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "风险", Binding = new Binding(nameof(CleanupCandidate.RiskLevel)), Width = 72 });
        grid.Columns.Add(new DataGridTextColumn { Header = "预计大小", Binding = new Binding(nameof(CleanupCandidate.EstimatedSizeBytes)), Width = 100 });
        grid.Columns.Add(new DataGridTextColumn { Header = "删除后果", Binding = new Binding(nameof(CleanupCandidate.Consequence)), Width = 190 });
        grid.Columns.Add(new DataGridCheckBoxColumn { Header = "不可恢复", Binding = new Binding(nameof(CleanupCandidate.Irreversible)), Width = 82, IsReadOnly = true });
        Grid.SetRow(grid, 2); root.Children.Add(grid);

        _irreversibleCheck = new CheckBox
        {
            Content = "我理解回收站清空后无法恢复",
            Visibility = preview.Candidates.Any(x => x.CleanupKind == XDiskInspector.Core.CleanupKind.RecycleBin) ? Visibility.Visible : Visibility.Collapsed,
            Foreground = (System.Windows.Media.Brush)Application.Current.Resources["RedBrush"],
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 6, 0, 8)
        };
        _irreversibleCheck.Checked += (_, _) => UpdateConfirmState(preview);
        _irreversibleCheck.Unchecked += (_, _) => UpdateConfirmState(preview);
        Grid.SetRow(_irreversibleCheck, 3); root.Children.Add(_irreversibleCheck);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "取消", MinWidth = 96 };
        cancel.Click += (_, _) => Close();
        _confirmButton = new Button { Content = "确认并执行", MinWidth = 128, Style = (Style)Application.Current.Resources["DangerButton"] };
        _confirmButton.Click += (_, _) => { Confirmed = true; DialogResult = true; };
        buttons.Children.Add(cancel); buttons.Children.Add(_confirmButton);
        Grid.SetRow(buttons, 4); root.Children.Add(buttons);

        Content = root;
        UpdateConfirmState(preview);
    }

    private void UpdateConfirmState(CleanupPreview preview)
    {
        var needsRecycleConfirmation = preview.Candidates.Any(x => x.CleanupKind == XDiskInspector.Core.CleanupKind.RecycleBin);
        _confirmButton.IsEnabled = !needsRecycleConfirmation || _irreversibleCheck.IsChecked == true;
    }
}
