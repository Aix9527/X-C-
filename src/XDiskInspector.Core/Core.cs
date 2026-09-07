using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace XDiskInspector.Core;

public enum RiskLevel { Low = 0, Medium = 1, High = 2, Critical = 3 }
public enum CleanupRecommendation { Suggested, Confirm, GuidanceOnly, Keep }
public enum ScanItemKind { File, Directory, Special }
public enum CleanupKind { None, File, RecycleBin }

public sealed record PathClassification(string RuleId, string Software, string Purpose, string Consequence, RiskLevel RiskLevel, CleanupRecommendation Recommendation, bool AllowCleanup, CleanupKind CleanupKind, int? MinAgeDays, string? Guidance, string MatchedRootPath, bool Irreversible = false);

public interface IPathClassifier { string RuleVersion { get; } PathClassification? Classify(string path); }

public sealed class ScanItem : INotifyPropertyChanged
{
    private bool _selected;
    public required string Path { get; init; }
    public required string Name { get; init; }
    public ScanItemKind Kind { get; init; }
    public long SizeBytes { get; set; }
    public string? Software { get; init; }
    public string? Purpose { get; init; }
    public string? Consequence { get; init; }
    public RiskLevel RiskLevel { get; init; }
    public CleanupRecommendation Recommendation { get; init; } = CleanupRecommendation.Keep;
    public string? CleanupRuleId { get; init; }
    public CleanupKind CleanupKind { get; init; }
    public bool Selectable { get; init; }
    public string? BlockedReason { get; init; }
    public DateTime? LastWriteTimeUtc { get; init; }
    public bool IsReparsePoint { get; init; }
    public int? MinAgeDays { get; init; }
    public string? Guidance { get; init; }
    public bool Irreversible { get; init; }
    public bool Selected { get => _selected; set { var safe = Selectable && value; if (_selected == safe) return; _selected = safe; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected))); } }
    public string RiskText => RiskLevel switch { RiskLevel.Low => "低", RiskLevel.Medium => "中", RiskLevel.High => "高", RiskLevel.Critical => "严重", _ => "未知" };
    public string RecommendationText => Recommendation switch { CleanupRecommendation.Suggested => "建议清理", CleanupRecommendation.Confirm => "需要确认", CleanupRecommendation.GuidanceOnly => "仅提供操作指导", _ => "保留" };
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed record DirectoryUsage(string Path, string Name, long SizeBytes);
public sealed record AccessIssue(string Path, string Reason);
public sealed class ScanReport
{
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
    public string ApplicationVersion { get; init; } = "1.0.0";
    public string RuleVersion { get; init; } = "unknown";
    public bool IsAdministrator { get; init; }
    public bool IsComplete { get; set; } = true;
    public string RootPath { get; init; } = string.Empty;
    public long DiskTotalBytes { get; init; }
    public long DiskUsedBytes { get; init; }
    public long DiskFreeBytes { get; init; }
    public long AccessibleLogicalBytes { get; set; }
    public long FileCount { get; set; }
    public TimeSpan Elapsed { get; set; }
    public List<DirectoryUsage> MainOccupancies { get; init; } = [];
    public List<ScanItem> HighlightedItems { get; init; } = [];
    public List<ScanItem> CleanupCandidates { get; init; } = [];
    public List<ScanItem> LargeFiles { get; init; } = [];
    public List<AccessIssue> AccessIssues { get; init; } = [];
    public double DiskUsedPercent => DiskTotalBytes <= 0 ? 0 : (double)DiskUsedBytes / DiskTotalBytes * 100d;
}
public sealed record ScanProgress(string CurrentPath, long FileCount, long LogicalBytes, TimeSpan Elapsed);
public sealed class ScanOptions { public required string RootPath { get; init; } public long LargeFileThresholdBytes { get; init; } = 500L * 1024 * 1024; public int MaxLargeFiles { get; init; } = 200; public int ProgressBatchSize { get; init; } = 250; }

internal sealed class BoundedLargeFileSet
{
    private readonly int _capacity;
    private readonly List<ScanItem> _items = [];
    public BoundedLargeFileSet(int capacity) => _capacity = Math.Max(1, capacity);
    public void Consider(ScanItem item)
    {
        if (_items.Count < _capacity) { _items.Add(item); return; }
        var minIndex = 0;
        for (var i = 1; i < _items.Count; i++) if (_items[i].SizeBytes < _items[minIndex].SizeBytes) minIndex = i;
        if (item.SizeBytes > _items[minIndex].SizeBytes) _items[minIndex] = item;
    }
    public List<ScanItem> ToDescendingList() => _items.OrderByDescending(x => x.SizeBytes).ToList();
}

public sealed class FileSystemScanner
{
    private readonly IPathClassifier _classifier;
    private readonly Func<bool> _administratorProbe;
    public FileSystemScanner(IPathClassifier classifier, Func<bool>? administratorProbe = null) { _classifier = classifier; _administratorProbe = administratorProbe ?? (() => false); }
    public Task<ScanReport> ScanAsync(ScanOptions options, IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default) => Task.Run(() => Scan(options, progress, cancellationToken), CancellationToken.None);

    private ScanReport Scan(ScanOptions options, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        var root = NormalizePath(options.RootPath);
        var sw = Stopwatch.StartNew();
        var top = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var recognizedSizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var recognized = new Dictionary<string, PathClassification>(StringComparer.OrdinalIgnoreCase);
        var cleanup = new Dictionary<string, ScanItem>(StringComparer.OrdinalIgnoreCase);
        var issues = new List<AccessIssue>();
        var largest = new BoundedLargeFileSet(options.MaxLargeFiles);
        long bytes = 0, count = 0;
        var complete = true;
        var (total, used, free) = TryGetDriveMetrics(root);
        var stack = new Stack<(string Dir, string Top)>(); stack.Push((root, string.Empty));
        try
        {
            while (stack.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (dir, inheritedTop) = stack.Pop();
                FileAttributes dirAttrs;
                try { dirAttrs = File.GetAttributes(dir); }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException) { issues.Add(new AccessIssue(dir, ex.Message)); continue; }
                var dirClass = _classifier.Classify(dir);
                if (dirClass is not null)
                {
                    recognized[dirClass.MatchedRootPath] = dirClass; recognizedSizes.TryAdd(dirClass.MatchedRootPath, 0);
                    if (dirClass.AllowCleanup && dirClass.CleanupKind == CleanupKind.RecycleBin) cleanup[dirClass.MatchedRootPath] = CreateItem(dirClass.MatchedRootPath, 0, ScanItemKind.Special, dirClass, dirAttrs.HasFlag(FileAttributes.ReparsePoint), null);
                }
                if (dirAttrs.HasFlag(FileAttributes.ReparsePoint)) { issues.Add(new AccessIssue(dir, "已跳过重解析点/目录联接/符号链接")); continue; }
                IEnumerable<string> entries;
                try { entries = Directory.EnumerateFileSystemEntries(dir); }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException) { issues.Add(new AccessIssue(dir, ex.Message)); continue; }
                using var en = entries.GetEnumerator();
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string entry;
                    try { if (!en.MoveNext()) break; entry = en.Current; }
                    catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { issues.Add(new AccessIssue(dir, ex.Message)); break; }
                    FileAttributes attrs;
                    try { attrs = File.GetAttributes(entry); }
                    catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException) { issues.Add(new AccessIssue(entry, ex.Message)); continue; }
                    if (attrs.HasFlag(FileAttributes.Directory)) { var key = inheritedTop; if (string.IsNullOrEmpty(key) && PathsEqual(dir, root)) key = entry; stack.Push((entry, key)); continue; }
                    long len; DateTime lastWrite;
                    try { var fi = new FileInfo(entry); len = fi.Length; lastWrite = fi.LastWriteTimeUtc; }
                    catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException) { issues.Add(new AccessIssue(entry, ex.Message)); continue; }
                    count++; bytes += len;
                    var topKey = string.IsNullOrEmpty(inheritedTop) ? "(根目录文件)" : inheritedTop;
                    top[topKey] = top.GetValueOrDefault(topKey) + len;
                    var c = _classifier.Classify(entry);
                    if (c is not null)
                    {
                        recognized[c.MatchedRootPath] = c; recognizedSizes[c.MatchedRootPath] = recognizedSizes.GetValueOrDefault(c.MatchedRootPath) + len;
                        if (c.AllowCleanup && c.CleanupKind == CleanupKind.File && (!c.MinAgeDays.HasValue || lastWrite <= DateTime.UtcNow.AddDays(-c.MinAgeDays.Value))) cleanup[entry] = CreateItem(entry, len, ScanItemKind.File, c, attrs.HasFlag(FileAttributes.ReparsePoint), lastWrite);
                    }
                    if (len >= options.LargeFileThresholdBytes) largest.Consider(c is null ? new ScanItem { Path = entry, Name = Path.GetFileName(entry), Kind = ScanItemKind.File, SizeBytes = len, LastWriteTimeUtc = lastWrite, IsReparsePoint = attrs.HasFlag(FileAttributes.ReparsePoint), Selectable = false, BlockedReason = "大文件不等同于垃圾；未通过清理允许列表" } : CreateItem(entry, len, ScanItemKind.File, c, attrs.HasFlag(FileAttributes.ReparsePoint), lastWrite));
                    if (progress is not null && count % Math.Max(1, options.ProgressBatchSize) == 0) progress.Report(new ScanProgress(dir, count, bytes, sw.Elapsed));
                }
            }
        }
        catch (OperationCanceledException) { complete = false; }
        sw.Stop();
        var main = top.Select(kv => new DirectoryUsage(kv.Key == "(根目录文件)" ? root : kv.Key, kv.Key == "(根目录文件)" ? kv.Key : Path.GetFileName(kv.Key.TrimEnd(Path.DirectorySeparatorChar)), kv.Value)).OrderByDescending(x => x.SizeBytes).ToList();
        var highlighted = recognized.Select(kv => { var item = CreateItem(kv.Key, recognizedSizes.GetValueOrDefault(kv.Key), ScanItemKind.Directory, kv.Value, false, null); item.Selected = false; return item; }).OrderByDescending(x => x.SizeBytes).ToList();
        foreach (var item in cleanup.Values) if (item.Recommendation == CleanupRecommendation.Suggested && item.RiskLevel <= RiskLevel.Medium) item.Selected = true;
        progress?.Report(new ScanProgress(root, count, bytes, sw.Elapsed));
        return new ScanReport { TimestampUtc = DateTime.UtcNow, ApplicationVersion = typeof(FileSystemScanner).Assembly.GetName().Version?.ToString(3) ?? "1.0.0", RuleVersion = _classifier.RuleVersion, IsAdministrator = _administratorProbe(), IsComplete = complete, RootPath = root, DiskTotalBytes = total, DiskUsedBytes = used, DiskFreeBytes = free, AccessibleLogicalBytes = bytes, FileCount = count, Elapsed = sw.Elapsed, MainOccupancies = main, HighlightedItems = highlighted, CleanupCandidates = cleanup.Values.OrderByDescending(x => x.SizeBytes).ToList(), LargeFiles = largest.ToDescendingList(), AccessIssues = issues };
    }

    private static ScanItem CreateItem(string path, long size, ScanItemKind kind, PathClassification c, bool reparse, DateTime? lastWrite)
    {
        var blocked = c.AllowCleanup ? null : c.Recommendation == CleanupRecommendation.GuidanceOnly ? "此项目只提供官方操作指导，不允许直接删除" : "规则库明确禁止自动清理";
        return new ScanItem { Path = path, Name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) is { Length: > 0 } name ? name : path, Kind = kind, SizeBytes = size, Software = c.Software, Purpose = c.Purpose, Consequence = c.Consequence, RiskLevel = c.RiskLevel, Recommendation = c.Recommendation, CleanupRuleId = c.AllowCleanup ? c.RuleId : null, CleanupKind = c.CleanupKind, Selectable = c.AllowCleanup && !reparse, BlockedReason = reparse ? "重解析点禁止自动清理" : blocked, LastWriteTimeUtc = lastWrite, IsReparsePoint = reparse, MinAgeDays = c.MinAgeDays, Guidance = c.Guidance, Irreversible = c.Irreversible };
    }
    private static (long Total, long Used, long Free) TryGetDriveMetrics(string path) { try { var root = Path.GetPathRoot(path); if (string.IsNullOrWhiteSpace(root)) return (0,0,0); var d = new DriveInfo(root); return (d.TotalSize, d.TotalSize - d.AvailableFreeSpace, d.AvailableFreeSpace); } catch { return (0,0,0); } }
    private static string NormalizePath(string path) { var full = Path.GetFullPath(path); var root = Path.GetPathRoot(full); if (!string.IsNullOrWhiteSpace(root) && PathsEqual(full, root)) return root; return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
    private static bool PathsEqual(string left, string right) => string.Equals(left.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), right.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
}

public sealed record OptimizationAdvice(string Title, string Detail, string ActionLabel, string ActionKind, string? Command = null, string? Arguments = null);
public static class OptimizationAdvisor
{
    public static IReadOnlyList<OptimizationAdvice> CreateDefaultAdvice() =>
    [
        new("页面文件（pagefile.sys）", "只检测与解释虚拟内存，不纳入一键清理。需要调整时请使用 Windows 高级系统设置。", "查看设置说明", "Guidance"),
        new("Windows 组件存储", "禁止手工删除 WinSxS 或 Windows\\Installer。仅允许通过官方 DISM 能力分析组件存储。", "分析组件存储", "Dism", "dism.exe", "/Online /Cleanup-Image /AnalyzeComponentStore"),
        new("Windows 组件官方清理", "仅在你明确点击后调用 DISM StartComponentCleanup；程序本身绝不遍历删除 WinSxS。", "运行官方组件清理", "Dism", "dism.exe", "/Online /Cleanup-Image /StartComponentCleanup"),
        new("已安装的软件", "不要直接删除安装目录；请进入 Windows 已安装的应用页面卸载。", "打开卸载页面", "Uri", "ms-settings:appsfeatures"),
        new("WSL / Docker 虚拟磁盘", "VHDX 可能包含完整 Linux 系统、镜像、容器和卷。仅提供官方清理/压缩指导，不直接删除。", "查看操作指导", "Guidance")
    ];
}
