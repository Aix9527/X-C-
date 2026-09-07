using System.Runtime.InteropServices;
using XDiskInspector.Core;
using XDiskInspector.Rules;

namespace XDiskInspector.Cleanup;

public sealed record PathSafetyResult(bool IsSafe, string? Reason, string? NormalizedPath)
{
    public static PathSafetyResult Safe(string path) => new(true, null, path);
    public static PathSafetyResult Blocked(string reason, string? path = null) => new(false, reason, path);
}

public sealed class SafePathPolicy
{
    private readonly string[] _forbiddenExactRoots;
    private readonly string[] _forbiddenTrees;

    public SafePathPolicy(IEnumerable<string>? extraForbiddenRoots = null)
    {
        var exactRoots = new List<string>();
        AddIfPresent(exactRoots, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        AddIfPresent(exactRoots, Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        AddIfPresent(exactRoots, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        AddIfPresent(exactRoots, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));

        var trees = new List<string>();
        AddIfPresent(trees, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        AddIfPresent(trees, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        AddIfPresent(trees, Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(profile)) AddIfPresent(trees, Path.Combine(profile, "Downloads"));
        if (extraForbiddenRoots is not null)
            foreach (var root in extraForbiddenRoots) AddIfPresent(trees, root);

        _forbiddenExactRoots = exactRoots.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        _forbiddenTrees = trees.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public PathSafetyResult Validate(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return PathSafetyResult.Blocked("路径为空");
        if (path.IndexOfAny(['*', '?']) >= 0) return PathSafetyResult.Blocked("禁止通配符清理目标");

        string full;
        try { full = Normalize(path); }
        catch (Exception ex) { return PathSafetyResult.Blocked($"无法解析绝对路径：{ex.Message}"); }

        var root = Path.GetPathRoot(full);
        if (!string.IsNullOrWhiteSpace(root) && PathsEqual(full, root)) return PathSafetyResult.Blocked("禁止删除磁盘根目录", full);

        foreach (var forbidden in _forbiddenExactRoots)
        {
            if (PathsEqual(full, forbidden)) return PathSafetyResult.Blocked("禁止删除受保护目录根", full);
        }

        foreach (var forbidden in _forbiddenTrees)
        {
            if (IsSameOrDescendant(full, forbidden)) return PathSafetyResult.Blocked("受保护个人或自定义目录及其子路径禁止自动清理", full);
        }

        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrWhiteSpace(windows))
        {
            var critical = new[]
            {
                Path.Combine(windows, "WinSxS"),
                Path.Combine(windows, "Installer"),
                Path.Combine(windows, "System32"),
                Path.Combine(windows, "System32", "DriverStore")
            };
            if (critical.Any(x => IsSameOrDescendant(full, x))) return PathSafetyResult.Blocked("Windows 关键目录禁止直接删除", full);
        }

        var segments = full.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (segments.Any(x => string.Equals(x, ".venv", StringComparison.OrdinalIgnoreCase) || string.Equals(x, "node_modules", StringComparison.OrdinalIgnoreCase)))
            return PathSafetyResult.Blocked("Python 虚拟环境或 node_modules 禁止自动清理", full);

        if (string.Equals(Path.GetExtension(full), ".vhdx", StringComparison.OrdinalIgnoreCase))
            return PathSafetyResult.Blocked("VHDX 虚拟磁盘禁止自动删除", full);

        try
        {
            if (File.Exists(full) && File.GetAttributes(full).HasFlag(FileAttributes.ReparsePoint))
                return PathSafetyResult.Blocked("重解析点目标禁止自动清理", full);
            if (Directory.Exists(full) && new DirectoryInfo(full).Attributes.HasFlag(FileAttributes.ReparsePoint))
                return PathSafetyResult.Blocked("重解析点目标禁止自动清理", full);

            var ancestor = Directory.Exists(full) ? new DirectoryInfo(full).Parent : new FileInfo(full).Directory;
            while (ancestor is not null)
            {
                if (ancestor.Exists && ancestor.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    return PathSafetyResult.Blocked("路径包含重解析点祖先，禁止自动清理", full);
                ancestor = ancestor.Parent;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return PathSafetyResult.Blocked($"无法验证路径安全属性：{ex.Message}", full);
        }

        return PathSafetyResult.Safe(full);
    }

    private static void AddIfPresent(List<string> roots, string? path)
    {
        if (!string.IsNullOrWhiteSpace(path)) roots.Add(Normalize(path));
    }

    private static string Normalize(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full);
        return !string.IsNullOrEmpty(root) && PathsEqual(full, root)
            ? root
            : full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool PathsEqual(string left, string right) => string.Equals(
        left.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
        right.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
        StringComparison.OrdinalIgnoreCase);

    private static bool IsSameOrDescendant(string path, string parent)
    {
        var p = Normalize(path);
        var basePath = Normalize(parent);
        return PathsEqual(p, basePath) || p.StartsWith(basePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record CleanupCandidate(
    string Path,
    string RuleId,
    string RuleVersion,
    long EstimatedSizeBytes,
    DateTime? ScannedLastWriteTimeUtc,
    CleanupKind CleanupKind,
    int? MinAgeDays,
    RiskLevel RiskLevel,
    string Consequence,
    bool Irreversible);

public sealed class CleanupPreview
{
    public required string RuleVersion { get; init; }
    public List<CleanupCandidate> Candidates { get; init; } = [];
    public List<string> Errors { get; init; } = [];
    public bool IsExecutable => Errors.Count == 0 && Candidates.Count > 0;
    public long EstimatedBytes => Candidates.Sum(x => x.EstimatedSizeBytes);
    public bool ContainsIrreversible => Candidates.Any(x => x.Irreversible);
}

public sealed class CleanupPreviewService
{
    private readonly PathRuleMatcher _matcher;
    private readonly SafePathPolicy _pathPolicy;

    public CleanupPreviewService(PathRuleMatcher matcher, SafePathPolicy pathPolicy)
    {
        _matcher = matcher;
        _pathPolicy = pathPolicy;
    }

    public CleanupPreview Build(ScanReport report, IEnumerable<ScanItem> items)
    {
        var preview = new CleanupPreview { RuleVersion = _matcher.RuleVersion };
        if (!report.IsComplete)
        {
            preview.Errors.Add("扫描结果不完整，禁止批量清理。请重新完成扫描。");
            return preview;
        }

        if (ScanReportRuntimeState.IsPersisted(report))
        {
            preview.Errors.Add("从磁盘载入的历史报告仅供查看，不能作为清理权限来源。请重新扫描当前机器后再清理。");
            return preview;
        }

        if (!string.Equals(report.RuleVersion, _matcher.RuleVersion, StringComparison.Ordinal))
        {
            preview.Errors.Add("扫描使用的规则版本已变化，请重新扫描后再清理。");
            return preview;
        }

        var valid = new List<CleanupCandidate>();
        foreach (var item in items.Where(x => x.Selected))
        {
            if (!item.Selectable)
            {
                preview.Errors.Add($"{item.Path}：项目不可选择，已拒绝进入清理清单。");
                continue;
            }
            if (string.IsNullOrWhiteSpace(item.CleanupRuleId))
            {
                preview.Errors.Add($"{item.Path}：缺少清理允许规则。");
                continue;
            }

            var rule = _matcher.GetRule(item.CleanupRuleId);
            var current = _matcher.Classify(item.Path);
            if (rule is null || !rule.AllowCleanup || current is null || !string.Equals(current.RuleId, item.CleanupRuleId, StringComparison.Ordinal))
            {
                preview.Errors.Add($"{item.Path}：当前规则库不再允许该目标清理。");
                continue;
            }

            if (current.CleanupKind == CleanupKind.File && item.Kind != ScanItemKind.File)
            {
                preview.Errors.Add($"{item.Path}：文件清理规则只接受扫描产生的文件项目。");
                continue;
            }
            if (current.CleanupKind == CleanupKind.RecycleBin && item.Kind != ScanItemKind.Special)
            {
                preview.Errors.Add($"{item.Path}：回收站规则只接受扫描产生的特殊项目。");
                continue;
            }

            var safety = _pathPolicy.Validate(item.Path);
            if (!safety.IsSafe || safety.NormalizedPath is null)
            {
                preview.Errors.Add($"{item.Path}：{safety.Reason}");
                continue;
            }

            valid.Add(new CleanupCandidate(
                safety.NormalizedPath,
                current.RuleId,
                _matcher.RuleVersion,
                item.SizeBytes,
                item.LastWriteTimeUtc,
                current.CleanupKind,
                current.MinAgeDays,
                current.RiskLevel,
                current.Consequence,
                current.Irreversible));
        }

        foreach (var candidate in Deduplicate(valid)) preview.Candidates.Add(candidate);
        return preview;
    }

    private static IEnumerable<CleanupCandidate> Deduplicate(IEnumerable<CleanupCandidate> candidates)
    {
        var kept = new List<CleanupCandidate>();
        foreach (var candidate in candidates.OrderBy(x => x.Path.Length).ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase))
        {
            if (kept.Any(parent => IsDescendant(candidate.Path, parent.Path))) continue;
            kept.Add(candidate);
        }
        return kept;
    }

    private static bool IsDescendant(string path, string parent)
    {
        if (string.Equals(path, parent, StringComparison.OrdinalIgnoreCase)) return true;
        return path.StartsWith(parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}

public enum CleanupItemStatus { Deleted, PartiallyDeleted, Skipped, Failed }
public sealed record CleanupItemResult(string Path, CleanupItemStatus Status, long FreedBytes, string Message);

public sealed class CleanupRunResult
{
    public List<CleanupItemResult> Items { get; init; } = [];
    public long ActualFreedBytes => Items.Sum(x => x.FreedBytes);
    public int DeletedCount => Items.Count(x => x.Status == CleanupItemStatus.Deleted);
    public int SkippedCount => Items.Count(x => x.Status == CleanupItemStatus.Skipped);
    public int FailedCount => Items.Count(x => x.Status == CleanupItemStatus.Failed);
    public bool Stopped { get; set; }
}

public sealed record CleanupExecutionOptions(bool AllowRecycleBinIrreversible = false);

public interface IRecycleBinService
{
    Task<(bool Success, string? Error)> EmptyAsync(string rootPath);
}

public sealed class ShellRecycleBinService : IRecycleBinService
{
    private const uint NoConfirmation = 0x00000001;
    private const uint NoProgressUi = 0x00000002;
    private const uint NoSound = 0x00000004;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(nint hwnd, string? pszRootPath, uint dwFlags);

    public Task<(bool Success, string? Error)> EmptyAsync(string rootPath)
    {
        if (!OperatingSystem.IsWindows()) return Task.FromResult<(bool, string?)>((false, "仅支持 Windows 回收站。"));
        if (string.IsNullOrWhiteSpace(rootPath)) return Task.FromResult<(bool, string?)>((false, "拒绝空回收站驱动器路径。"));

        string normalizedRoot;
        try
        {
            normalizedRoot = Path.GetPathRoot(Path.GetFullPath(rootPath)) ?? string.Empty;
        }
        catch (Exception ex)
        {
            return Task.FromResult<(bool, string?)>((false, $"无法解析回收站驱动器路径：{ex.Message}"));
        }

        if (string.IsNullOrWhiteSpace(normalizedRoot))
            return Task.FromResult<(bool, string?)>((false, "无法确定回收站所在驱动器。"));

        var result = SHEmptyRecycleBin(0, normalizedRoot, NoConfirmation | NoProgressUi | NoSound);
        return Task.FromResult(result == 0
            ? (true, (string?)null)
            : (false, $"SHEmptyRecycleBin 返回错误码 0x{result:X8}"));
    }
}

public sealed class CleanupExecutor
{
    private readonly PathRuleMatcher _matcher;
    private readonly SafePathPolicy _pathPolicy;
    private readonly IRecycleBinService _recycleBinService;

    public CleanupExecutor(PathRuleMatcher matcher, SafePathPolicy pathPolicy, IRecycleBinService? recycleBinService = null)
    {
        _matcher = matcher;
        _pathPolicy = pathPolicy;
        _recycleBinService = recycleBinService ?? new ShellRecycleBinService();
    }

    public async Task<CleanupRunResult> ExecuteAsync(CleanupPreview preview, CleanupExecutionOptions options, CancellationToken stopAfterCurrentItem = default)
    {
        var run = new CleanupRunResult();
        if (!preview.IsExecutable)
        {
            foreach (var error in preview.Errors) run.Items.Add(new CleanupItemResult("(预览)", CleanupItemStatus.Skipped, 0, error));
            return run;
        }

        foreach (var candidate in preview.Candidates)
        {
            if (stopAfterCurrentItem.IsCancellationRequested)
            {
                run.Stopped = true;
                break;
            }

            run.Items.Add(await ExecuteOneAsync(candidate, options));
        }

        return run;
    }

    private async Task<CleanupItemResult> ExecuteOneAsync(CleanupCandidate candidate, CleanupExecutionOptions options)
    {
        if (!string.Equals(candidate.RuleVersion, _matcher.RuleVersion, StringComparison.Ordinal))
            return new CleanupItemResult(candidate.Path, CleanupItemStatus.Skipped, 0, "规则版本已变化，请重新扫描。");

        var rule = _matcher.GetRule(candidate.RuleId);
        var classification = _matcher.Classify(candidate.Path);
        if (rule is null || !rule.AllowCleanup || classification is null || !string.Equals(classification.RuleId, candidate.RuleId, StringComparison.Ordinal))
            return new CleanupItemResult(candidate.Path, CleanupItemStatus.Skipped, 0, "允许列表复核失败。");

        if (classification.CleanupKind != candidate.CleanupKind ||
            classification.MinAgeDays != candidate.MinAgeDays ||
            classification.Irreversible != candidate.Irreversible ||
            classification.RiskLevel != candidate.RiskLevel)
            return new CleanupItemResult(candidate.Path, CleanupItemStatus.Skipped, 0, "清理规则属性与当前规则库不一致，已拒绝执行。");

        var safety = _pathPolicy.Validate(candidate.Path);
        if (!safety.IsSafe || safety.NormalizedPath is null)
            return new CleanupItemResult(candidate.Path, CleanupItemStatus.Skipped, 0, safety.Reason ?? "路径安全复核失败。");

        if (classification.CleanupKind == CleanupKind.RecycleBin)
        {
            if (!classification.Irreversible || !options.AllowRecycleBinIrreversible)
                return new CleanupItemResult(candidate.Path, CleanupItemStatus.Skipped, 0, "未勾选“我理解回收站清空后无法恢复”。");

            var recycleRoot = Path.GetPathRoot(safety.NormalizedPath);
            if (string.IsNullOrWhiteSpace(recycleRoot))
                return new CleanupItemResult(candidate.Path, CleanupItemStatus.Skipped, 0, "无法确定回收站所在驱动器，已拒绝执行。");

            var beforeFree = TryGetAvailableFreeSpace(recycleRoot);
            var recycleResult = await _recycleBinService.EmptyAsync(recycleRoot);
            if (!recycleResult.Success)
                return new CleanupItemResult(candidate.Path, CleanupItemStatus.Failed, 0, recycleResult.Error ?? "回收站清理失败。");

            var afterFree = TryGetAvailableFreeSpace(recycleRoot);
            var measuredFreed = beforeFree.HasValue && afterFree.HasValue
                ? Math.Max(0, afterFree.Value - beforeFree.Value)
                : 0;
            var message = measuredFreed > 0
                ? $"已清空 {recycleRoot} 回收站，并检测到实际释放 {measuredFreed} 字节。"
                : $"已清空 {recycleRoot} 回收站；Windows 未立即报告可测的可用空间变化。";
            return new CleanupItemResult(candidate.Path, CleanupItemStatus.Deleted, measuredFreed, message);
        }

        if (classification.CleanupKind != CleanupKind.File)
            return new CleanupItemResult(candidate.Path, CleanupItemStatus.Skipped, 0, "当前版本不直接删除该类型目标。");

        try
        {
            if (!File.Exists(candidate.Path)) return new CleanupItemResult(candidate.Path, CleanupItemStatus.Skipped, 0, "文件已不存在。");

            var attributes = File.GetAttributes(candidate.Path);
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
                return new CleanupItemResult(candidate.Path, CleanupItemStatus.Skipped, 0, "文件已变为重解析点。");

            var currentLastWrite = File.GetLastWriteTimeUtc(candidate.Path);
            if (candidate.ScannedLastWriteTimeUtc.HasValue && Math.Abs((currentLastWrite - candidate.ScannedLastWriteTimeUtc.Value).TotalSeconds) > 2)
                return new CleanupItemResult(candidate.Path, CleanupItemStatus.Skipped, 0, "文件在扫描后发生变化。");

            if (classification.MinAgeDays.HasValue && currentLastWrite > DateTime.UtcNow.AddDays(-classification.MinAgeDays.Value))
                return new CleanupItemResult(candidate.Path, CleanupItemStatus.Skipped, 0, $"文件未达到 {classification.MinAgeDays.Value} 天最小保留时间。");

            var finalSafety = _pathPolicy.Validate(candidate.Path);
            if (!finalSafety.IsSafe || finalSafety.NormalizedPath is null ||
                !string.Equals(finalSafety.NormalizedPath, safety.NormalizedPath, StringComparison.OrdinalIgnoreCase))
                return new CleanupItemResult(candidate.Path, CleanupItemStatus.Skipped, 0, finalSafety.Reason ?? "删除前最终路径安全复核失败。");

            var before = new FileInfo(candidate.Path).Length;
            File.Delete(candidate.Path);
            return new CleanupItemResult(candidate.Path, CleanupItemStatus.Deleted, before, "已删除。");
        }
        catch (UnauthorizedAccessException ex)
        {
            return new CleanupItemResult(candidate.Path, CleanupItemStatus.Failed, 0, $"权限不足：{ex.Message}");
        }
        catch (IOException ex)
        {
            return new CleanupItemResult(candidate.Path, CleanupItemStatus.Failed, 0, $"文件正在使用或 I/O 失败：{ex.Message}");
        }
        catch (Exception ex)
        {
            return new CleanupItemResult(candidate.Path, CleanupItemStatus.Failed, 0, ex.Message);
        }
    }

    private static long? TryGetAvailableFreeSpace(string rootPath)
    {
        try { return new DriveInfo(rootPath).AvailableFreeSpace; }
        catch { return null; }
    }
}
