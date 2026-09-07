using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows;
using System.Windows.Data;
using Microsoft.Win32;
using XDiskInspector.Cleanup;
using XDiskInspector.Core;
using XDiskInspector.Reporting;
using XDiskInspector.Rules;

namespace XDiskInspector.App;

public sealed class MainViewModel : ObservableObject
{
    private readonly RuleLibrary _ruleLibrary;
    private readonly PathRuleMatcher _matcher;
    private readonly FileSystemScanner _scanner;
    private readonly SafePathPolicy _pathPolicy;
    private readonly CleanupPreviewService _previewService;
    private readonly CleanupExecutor _cleanupExecutor;
    private readonly JsonReportWriter _jsonWriter = new();
    private readonly HtmlReportWriter _htmlWriter = new();
    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _cleanupCts;
    private ScanReport? _currentReport;
    private int _selectedPageIndex;
    private bool _isScanning;
    private bool _isCleanupRunning;
    private string _scanStatus = "尚未扫描";
    private string _currentPath = "—";
    private string _fileCountText = "0";
    private string _scannedSizeText = "0 B";
    private string _elapsedText = "0.0 秒";
    private long _diskTotal;
    private long _diskUsed;
    private long _diskFree;
    private double _largeFileThresholdMb = 500;
    private double _minimumSizeMb;
    private string _riskFilter = "全部";
    private string _recommendationFilter = "全部";
    private string _softwareFilter = string.Empty;

    public MainViewModel()
    {
        _ruleLibrary = RuleLibrary.LoadDefault();
        _matcher = new PathRuleMatcher(_ruleLibrary);
        _scanner = new FileSystemScanner(_matcher, IsAdministrator);
        _pathPolicy = new SafePathPolicy();
        _previewService = new CleanupPreviewService(_matcher, _pathPolicy);
        _cleanupExecutor = new CleanupExecutor(_matcher, _pathPolicy);

        CandidateView = CollectionViewSource.GetDefaultView(CleanupItems);
        CandidateView.Filter = CandidateFilter;
        CandidateView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ScanItem.RecommendationText)));
        HighlightView = CollectionViewSource.GetDefaultView(HighlightedItems);
        HighlightView.Filter = HighlightFilter;
        LargeFileView = CollectionViewSource.GetDefaultView(LargeFiles);
        LargeFileView.Filter = LargeFileFilter;

        NavigateCommand = new RelayCommand(p => SelectedPageIndex = Convert.ToInt32(p));
        StartScanCommand = new AsyncRelayCommand(_ => StartScanAsync(), _ => !IsScanning && !IsCleanupRunning);
        CancelScanCommand = new RelayCommand(_ => _scanCts?.Cancel(), _ => IsScanning);
        LoadLastReportCommand = new AsyncRelayCommand(_ => LoadLastReportAsync(), _ => !IsScanning && !IsCleanupRunning);
        SelectSuggestedCommand = new RelayCommand(_ => SelectSuggested(), _ => IsSelectionEditable);
        ClearSelectionCommand = new RelayCommand(_ => ClearSelection(), _ => IsSelectionEditable);
        ExecuteCleanupCommand = new AsyncRelayCommand(_ => ExecuteCleanupAsync(), _ => CanExecuteCleanup);
        StopCleanupCommand = new RelayCommand(_ => _cleanupCts?.Cancel(), _ => IsCleanupRunning);
        ExportJsonCommand = new AsyncRelayCommand(_ => ExportJsonAsync(), _ => CurrentReport is not null);
        ExportHtmlCommand = new AsyncRelayCommand(_ => ExportHtmlAsync(), _ => CurrentReport is not null);
        RunOptimizationActionCommand = new RelayCommand(RunOptimizationAction);

        foreach (var advice in OptimizationAdvisor.CreateDefaultAdvice()) OptimizationItems.Add(advice);
        RefreshDriveMetrics();
    }

    public ObservableCollection<DirectoryUsage> MainOccupancies { get; } = [];
    public ObservableCollection<ScanItem> HighlightedItems { get; } = [];
    public ObservableCollection<ScanItem> LargeFiles { get; } = [];
    public ObservableCollection<ScanItem> CleanupItems { get; } = [];
    public ObservableCollection<ScanItem> GuidanceItems { get; } = [];
    public ObservableCollection<CleanupItemResult> CleanupResults { get; } = [];
    public ObservableCollection<OptimizationAdvice> OptimizationItems { get; } = [];

    public ICollectionView CandidateView { get; }
    public ICollectionView HighlightView { get; }
    public ICollectionView LargeFileView { get; }

    public IReadOnlyList<string> RiskFilters { get; } = ["全部", "低", "中", "高", "严重"];
    public IReadOnlyList<string> RecommendationFilters { get; } = ["全部", "建议清理", "需要确认", "仅提供操作指导", "保留"];

    public RelayCommand NavigateCommand { get; }
    public AsyncRelayCommand StartScanCommand { get; }
    public RelayCommand CancelScanCommand { get; }
    public AsyncRelayCommand LoadLastReportCommand { get; }
    public RelayCommand SelectSuggestedCommand { get; }
    public RelayCommand ClearSelectionCommand { get; }
    public AsyncRelayCommand ExecuteCleanupCommand { get; }
    public RelayCommand StopCleanupCommand { get; }
    public AsyncRelayCommand ExportJsonCommand { get; }
    public AsyncRelayCommand ExportHtmlCommand { get; }
    public RelayCommand RunOptimizationActionCommand { get; }

    public ScanReport? CurrentReport
    {
        get => _currentReport;
        private set
        {
            if (!SetProperty(ref _currentReport, value)) return;
            Raise(nameof(IsCurrentReportRuleVersion));
            Raise(nameof(IsPersistedReport));
            Raise(nameof(IsSelectionEditable));
            Raise(nameof(ScanCompletenessText));
            Raise(nameof(CanExecuteCleanup));
            RaiseCommandStates();
        }
    }
    public int SelectedPageIndex { get => _selectedPageIndex; set => SetProperty(ref _selectedPageIndex, value); }
    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (!SetProperty(ref _isScanning, value)) return;
            Raise(nameof(IsNotScanning));
            Raise(nameof(IsSelectionEditable));
            Raise(nameof(CanExecuteCleanup));
            RaiseCommandStates();
        }
    }
    public bool IsNotScanning => !IsScanning;
    public bool IsCleanupRunning
    {
        get => _isCleanupRunning;
        private set
        {
            if (!SetProperty(ref _isCleanupRunning, value)) return;
            Raise(nameof(IsSelectionEditable));
            Raise(nameof(CanExecuteCleanup));
            RaiseCommandStates();
        }
    }
    public bool IsSelectionEditable => !IsCleanupRunning && !IsScanning && !IsPersistedReport;
    public bool IsCurrentReportRuleVersion => CurrentReport is not null && string.Equals(CurrentReport.RuleVersion, _matcher.RuleVersion, StringComparison.Ordinal);
    public bool IsPersistedReport => ScanReportRuntimeState.IsPersisted(CurrentReport);
    public bool CanExecuteCleanup => CurrentReport?.IsComplete == true && IsCurrentReportRuleVersion && !IsPersistedReport && SelectedCount > 0 && !IsScanning && !IsCleanupRunning;
    public string ScanStatus { get => _scanStatus; private set => SetProperty(ref _scanStatus, value); }
    public string CurrentPath { get => _currentPath; private set => SetProperty(ref _currentPath, value); }
    public string FileCountText { get => _fileCountText; private set => SetProperty(ref _fileCountText, value); }
    public string ScannedSizeText { get => _scannedSizeText; private set => SetProperty(ref _scannedSizeText, value); }
    public string ElapsedText { get => _elapsedText; private set => SetProperty(ref _elapsedText, value); }

    public string DiskTotalText => ByteFormatter.Format(_diskTotal);
    public string DiskUsedText => ByteFormatter.Format(_diskUsed);
    public string DiskFreeText => ByteFormatter.Format(_diskFree);
    public double DiskUsedPercent => _diskTotal <= 0 ? 0 : (double)_diskUsed / _diskTotal * 100d;
    public string DiskUsedPercentText => $"{DiskUsedPercent:0.0}%";
    public string AccessibleLogicalText => CurrentReport is null ? "—" : ByteFormatter.Format(CurrentReport.AccessibleLogicalBytes);
    public string RuleVersionText => _ruleLibrary.Version;
    public string ScanCompletenessText => CurrentReport is null
        ? "尚无报告"
        : !CurrentReport.IsComplete
            ? "不完整报告，已禁用批量清理"
            : IsPersistedReport
                ? "历史报告仅供查看；要执行清理请重新扫描当前机器"
                : !IsCurrentReportRuleVersion
                    ? $"报告规则版本 {CurrentReport.RuleVersion} 已过期；当前规则 {_matcher.RuleVersion}，请重新扫描"
                    : "完整报告，可进行安全清理";

    public double LargeFileThresholdMb { get => _largeFileThresholdMb; set { if (SetProperty(ref _largeFileThresholdMb, Math.Clamp(value, 1, 1024 * 1024))) LargeFileView.Refresh(); } }
    public double MinimumSizeMb { get => _minimumSizeMb; set { if (SetProperty(ref _minimumSizeMb, Math.Max(0, value))) RefreshFilters(); } }
    public string RiskFilter { get => _riskFilter; set { if (SetProperty(ref _riskFilter, value)) RefreshFilters(); } }
    public string RecommendationFilter { get => _recommendationFilter; set { if (SetProperty(ref _recommendationFilter, value)) RefreshFilters(); } }
    public string SoftwareFilter { get => _softwareFilter; set { if (SetProperty(ref _softwareFilter, value ?? string.Empty)) RefreshFilters(); } }

    public int SelectedCount => CleanupItems.Count(x => x.Selected);
    public string SelectedBytesText => ByteFormatter.Format(CleanupItems.Where(x => x.Selected).Sum(x => x.SizeBytes));
    public string HighestSelectedRiskText => SelectedCount == 0 ? "无" : CleanupItems.Where(x => x.Selected).Max(x => x.RiskLevel) switch
    {
        RiskLevel.Low => "低", RiskLevel.Medium => "中", RiskLevel.High => "高", RiskLevel.Critical => "严重", _ => "无"
    };

    private string LastReportPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XDiskInspector", "last-report.json");

    private async Task StartScanAsync()
    {
        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource();
        IsScanning = true;
        ScanStatus = "正在扫描 C 盘元数据…";
        CurrentPath = @"C:\";
        FileCountText = "0"; ScannedSizeText = "0 B"; ElapsedText = "0.0 秒";
        ClearReportCollections(); RefreshDriveMetrics();
        try
        {
            var progress = new Progress<ScanProgress>(p => { CurrentPath = p.CurrentPath; FileCountText = p.FileCount.ToString("N0"); ScannedSizeText = ByteFormatter.Format(p.LogicalBytes); ElapsedText = $"{p.Elapsed.TotalSeconds:0.0} 秒"; });
            var report = await _scanner.ScanAsync(new ScanOptions { RootPath = @"C:\", LargeFileThresholdBytes = (long)(LargeFileThresholdMb * 1024 * 1024), MaxLargeFiles = 300, ProgressBatchSize = 300 }, progress, _scanCts.Token);
            ApplyReport(report);
            ScanStatus = report.IsComplete ? "扫描完成" : "扫描已取消：报告不完整，批量清理已禁用";
            await TrySaveLastReportAsync(report);
            SelectedPageIndex = 1;
        }
        catch (Exception ex) { ScanStatus = "扫描失败"; MessageBox.Show($"扫描未完成：{ex.Message}", "X C盘巡检官", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { IsScanning = false; }
    }

    private async Task LoadLastReportAsync()
    {
        try
        {
            if (!File.Exists(LastReportPath)) { MessageBox.Show("还没有保存过扫描报告。请先执行一次扫描。", "载入上次报告", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            var report = await _jsonWriter.ReadAsync(LastReportPath);
            ApplyReport(report);
            ScanStatus = !report.IsComplete
                ? "已载入上次不完整报告（仅供查看）"
                : !IsCurrentReportRuleVersion
                    ? "已载入旧规则版本历史报告（仅供查看，请重新扫描）"
                    : "已载入历史报告（仅供查看；清理前必须重新扫描当前机器）";
            SelectedPageIndex = 1;
        }
        catch (Exception ex) { MessageBox.Show($"无法载入上次报告：{ex.Message}", "载入上次报告", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void ApplyReport(ScanReport report)
    {
        CurrentReport = report; _diskTotal = report.DiskTotalBytes; _diskUsed = report.DiskUsedBytes; _diskFree = report.DiskFreeBytes;
        Raise(nameof(DiskTotalText)); Raise(nameof(DiskUsedText)); Raise(nameof(DiskFreeText)); Raise(nameof(DiskUsedPercent)); Raise(nameof(DiskUsedPercentText)); Raise(nameof(AccessibleLogicalText)); Raise(nameof(ScanCompletenessText));
        ClearReportCollections();
        foreach (var item in report.MainOccupancies) MainOccupancies.Add(item);
        foreach (var item in report.HighlightedItems) HighlightedItems.Add(item);
        foreach (var item in report.LargeFiles) LargeFiles.Add(item);
        foreach (var item in report.CleanupCandidates) { item.PropertyChanged += CleanupItemOnPropertyChanged; CleanupItems.Add(item); }
        foreach (var item in report.HighlightedItems.Where(x => x.Recommendation == CleanupRecommendation.GuidanceOnly)) GuidanceItems.Add(item);
        RefreshFilters(); RaiseSelectionSummary();
    }

    private void ClearReportCollections()
    {
        foreach (var item in CleanupItems) item.PropertyChanged -= CleanupItemOnPropertyChanged;
        MainOccupancies.Clear(); HighlightedItems.Clear(); LargeFiles.Clear(); CleanupItems.Clear(); GuidanceItems.Clear(); CleanupResults.Clear(); RaiseSelectionSummary();
    }

    private void CleanupItemOnPropertyChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(ScanItem.Selected)) RaiseSelectionSummary(); }
    private void SelectSuggested() { SelectionPolicy.SelectSuggested(CandidateView.Cast<ScanItem>()); RaiseSelectionSummary(); }
    private void ClearSelection() { foreach (var item in CleanupItems) item.Selected = false; RaiseSelectionSummary(); }

    private async Task ExecuteCleanupAsync()
    {
        if (CurrentReport is null) return;
        var preview = _previewService.Build(CurrentReport, CleanupItems);
        if (preview.Errors.Count > 0) { MessageBox.Show(string.Join(Environment.NewLine, preview.Errors.Take(10)), "清理预览被安全规则阻止", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        if (preview.Candidates.Count == 0) { MessageBox.Show("没有通过最终安全复核的已选项目。", "安全清理", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var dialog = new CleanupConfirmationWindow(preview) { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true || !dialog.Confirmed) return;
        _cleanupCts?.Dispose(); _cleanupCts = new CancellationTokenSource(); IsCleanupRunning = true; CleanupResults.Clear();
        try
        {
            var result = await _cleanupExecutor.ExecuteAsync(preview, new CleanupExecutionOptions(dialog.AllowRecycleBinIrreversible), _cleanupCts.Token);
            foreach (var item in result.Items) CleanupResults.Add(item);
            foreach (var deleted in result.Items.Where(x => x.Status == CleanupItemStatus.Deleted)) { var source = CleanupItems.FirstOrDefault(x => string.Equals(x.Path, deleted.Path, StringComparison.OrdinalIgnoreCase)); if (source is not null) source.Selected = false; }
            RaiseSelectionSummary();
            var restart = result.RestartRequiredCount > 0 ? $"\n需要重启：{result.RestartRequiredCount} 项。" : "\n需要重启：0 项。";
            var stopped = result.Stopped ? "\n用户已停止后续项目。" : string.Empty;
            MessageBox.Show($"清理完成：成功 {result.DeletedCount}，跳过 {result.SkippedCount}，失败 {result.FailedCount}。\n实际释放：{ByteFormatter.Format(result.ActualFreedBytes)}{restart}{stopped}", "安全清理结果", MessageBoxButton.OK, result.FailedCount > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
        finally { IsCleanupRunning = false; }
    }

    private async Task ExportJsonAsync()
    {
        if (CurrentReport is null) return;
        var dialog = new SaveFileDialog { Filter = "JSON 报告 (*.json)|*.json", FileName = $"XDiskInspector-{DateTime.Now:yyyyMMdd-HHmm}.json" };
        if (dialog.ShowDialog() != true) return;
        try { await _jsonWriter.WriteAsync(CurrentReport, dialog.FileName); } catch (Exception ex) { MessageBox.Show($"报告写入失败，请重新选择目录：{ex.Message}", "导出失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private async Task ExportHtmlAsync()
    {
        if (CurrentReport is null) return;
        var dialog = new SaveFileDialog { Filter = "HTML 报告 (*.html)|*.html", FileName = $"XDiskInspector-{DateTime.Now:yyyyMMdd-HHmm}.html" };
        if (dialog.ShowDialog() != true) return;
        try { await _htmlWriter.WriteAsync(CurrentReport, dialog.FileName); } catch (Exception ex) { MessageBox.Show($"报告写入失败，请重新选择目录：{ex.Message}", "导出失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void RunOptimizationAction(object? parameter)
    {
        if (parameter is not OptimizationAdvice advice) return;
        try
        {
            if (advice.ActionKind == "Uri" && !string.IsNullOrWhiteSpace(advice.Command)) { Process.Start(new ProcessStartInfo(advice.Command) { UseShellExecute = true }); return; }
            if (advice.ActionKind == "Dism" && !string.IsNullOrWhiteSpace(advice.Command)) { Process.Start(new ProcessStartInfo(advice.Command, advice.Arguments ?? string.Empty) { UseShellExecute = true, Verb = "runas" }); return; }
            MessageBox.Show(advice.Detail + (string.IsNullOrWhiteSpace(advice.Command) ? string.Empty : $"\n\n官方命令：{advice.Command} {advice.Arguments}"), advice.Title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Win32Exception ex) { MessageBox.Show($"操作未执行：{ex.Message}", advice.Title, MessageBoxButton.OK, MessageBoxImage.Information); }
    }

    private async Task TrySaveLastReportAsync(ScanReport report)
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(LastReportPath)!); await _jsonWriter.WriteAsync(report, LastReportPath); }
        catch (Exception ex) { ScanStatus += $"；上次报告保存失败：{ex.Message}"; }
    }

    private void RefreshDriveMetrics()
    {
        try { var drive = new DriveInfo("C"); _diskTotal = drive.TotalSize; _diskFree = drive.AvailableFreeSpace; _diskUsed = _diskTotal - _diskFree; }
        catch { _diskTotal = _diskUsed = _diskFree = 0; }
        Raise(nameof(DiskTotalText)); Raise(nameof(DiskUsedText)); Raise(nameof(DiskFreeText)); Raise(nameof(DiskUsedPercent)); Raise(nameof(DiskUsedPercentText));
    }

    private bool CandidateFilter(object obj) => obj is ScanItem item && CommonFilter(item);
    private bool HighlightFilter(object obj) => obj is ScanItem item && CommonFilter(item);
    private bool LargeFileFilter(object obj) => obj is ScanItem item && item.SizeBytes >= LargeFileThresholdMb * 1024 * 1024 && CommonFilter(item);
    private bool CommonFilter(ScanItem item)
    {
        if (item.SizeBytes < MinimumSizeMb * 1024 * 1024) return false;
        if (!string.IsNullOrWhiteSpace(SoftwareFilter) && !(item.Software ?? string.Empty).Contains(SoftwareFilter, StringComparison.OrdinalIgnoreCase)) return false;
        if (RiskFilter != "全部" && item.RiskText != RiskFilter) return false;
        if (RecommendationFilter != "全部" && item.RecommendationText != RecommendationFilter) return false;
        return true;
    }
    private void RefreshFilters() { CandidateView.Refresh(); HighlightView.Refresh(); LargeFileView.Refresh(); }
    private void RaiseSelectionSummary() { Raise(nameof(SelectedCount)); Raise(nameof(SelectedBytesText)); Raise(nameof(HighestSelectedRiskText)); Raise(nameof(CanExecuteCleanup)); ExecuteCleanupCommand.RaiseCanExecuteChanged(); }
    private void RaiseCommandStates()
    {
        StartScanCommand.RaiseCanExecuteChanged(); CancelScanCommand.RaiseCanExecuteChanged(); LoadLastReportCommand.RaiseCanExecuteChanged(); SelectSuggestedCommand.RaiseCanExecuteChanged(); ClearSelectionCommand.RaiseCanExecuteChanged(); ExecuteCleanupCommand.RaiseCanExecuteChanged(); StopCleanupCommand.RaiseCanExecuteChanged(); ExportJsonCommand.RaiseCanExecuteChanged(); ExportHtmlCommand.RaiseCanExecuteChanged();
    }
    private static bool IsAdministrator()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try { using var identity = WindowsIdentity.GetCurrent(); return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator); } catch { return false; }
    }
}
