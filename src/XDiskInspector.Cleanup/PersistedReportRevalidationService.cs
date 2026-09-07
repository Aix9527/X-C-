using XDiskInspector.Core;
using XDiskInspector.Rules;

namespace XDiskInspector.Cleanup;

public sealed class PersistedReportRevalidationService
{
    private readonly PathRuleMatcher _matcher;
    private readonly SafePathPolicy _pathPolicy;

    public PersistedReportRevalidationService(PathRuleMatcher matcher, SafePathPolicy pathPolicy)
    {
        _matcher = matcher;
        _pathPolicy = pathPolicy;
    }

    public CleanupPreview Build(ScanReport report, IEnumerable<ScanItem> selectedItems)
    {
        var preview = new CleanupPreview { RuleVersion = _matcher.RuleVersion };

        if (!ScanReportRuntimeState.IsPersisted(report))
        {
            preview.Errors.Add("此复核通道只用于从磁盘载入的历史报告。");
            return preview;
        }

        if (!report.IsComplete)
        {
            preview.Errors.Add("历史报告不完整，禁止据此执行批量清理。请重新扫描。");
            return preview;
        }

        var valid = new List<CleanupCandidate>();
        foreach (var item in selectedItems.Where(x => x.Selected))
        {
            var safety = _pathPolicy.Validate(item.Path);
            if (!safety.IsSafe || safety.NormalizedPath is null)
            {
                preview.Errors.Add($"{item.Path}：{safety.Reason}");
                continue;
            }

            var current = _matcher.Classify(safety.NormalizedPath);
            if (current is null || !current.AllowCleanup || current.CleanupKind == CleanupKind.None)
            {
                preview.Errors.Add($"{item.Path}：当前规则库不允许该目标清理。");
                continue;
            }

            var rule = _matcher.GetRule(current.RuleId);
            if (rule is null || !rule.AllowCleanup)
            {
                preview.Errors.Add($"{item.Path}：当前清理允许规则不存在或已禁用。");
                continue;
            }

            try
            {
                switch (current.CleanupKind)
                {
                    case CleanupKind.File:
                    {
                        if (!File.Exists(safety.NormalizedPath))
                        {
                            preview.Errors.Add($"{item.Path}：文件当前已不存在。");
                            continue;
                        }

                        var attributes = File.GetAttributes(safety.NormalizedPath);
                        if (attributes.HasFlag(FileAttributes.ReparsePoint))
                        {
                            preview.Errors.Add($"{item.Path}：文件当前是重解析点，已拒绝。");
                            continue;
                        }

                        var info = new FileInfo(safety.NormalizedPath);
                        var lastWrite = info.LastWriteTimeUtc;
                        if (current.MinAgeDays.HasValue && lastWrite > DateTime.UtcNow.AddDays(-current.MinAgeDays.Value))
                        {
                            preview.Errors.Add($"{item.Path}：文件当前未达到 {current.MinAgeDays.Value} 天最小保留时间。");
                            continue;
                        }

                        valid.Add(new CleanupCandidate(
                            safety.NormalizedPath,
                            current.RuleId,
                            _matcher.RuleVersion,
                            info.Length,
                            lastWrite,
                            current.CleanupKind,
                            current.MinAgeDays,
                            current.RiskLevel,
                            current.Consequence,
                            current.Irreversible));
                        break;
                    }
                    case CleanupKind.RecycleBin:
                    {
                        if (!Directory.Exists(safety.NormalizedPath))
                        {
                            preview.Errors.Add($"{item.Path}：回收站路径当前不存在。");
                            continue;
                        }

                        valid.Add(new CleanupCandidate(
                            safety.NormalizedPath,
                            current.RuleId,
                            _matcher.RuleVersion,
                            Math.Max(0, item.SizeBytes),
                            null,
                            current.CleanupKind,
                            current.MinAgeDays,
                            current.RiskLevel,
                            current.Consequence,
                            current.Irreversible));
                        break;
                    }
                    default:
                        preview.Errors.Add($"{item.Path}：当前类型不支持自动清理。");
                        break;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                preview.Errors.Add($"{item.Path}：实时复核失败：{ex.Message}");
            }
        }

        foreach (var candidate in Deduplicate(valid)) preview.Candidates.Add(candidate);
        return preview;
    }

    private static IEnumerable<CleanupCandidate> Deduplicate(IEnumerable<CleanupCandidate> candidates)
    {
        var kept = new List<CleanupCandidate>();
        foreach (var candidate in candidates.OrderBy(x => x.Path.Length).ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase))
        {
            if (kept.Any(parent => IsSameOrDescendant(candidate.Path, parent.Path))) continue;
            kept.Add(candidate);
        }
        return kept;
    }

    private static bool IsSameOrDescendant(string path, string parent)
    {
        if (string.Equals(path, parent, StringComparison.OrdinalIgnoreCase)) return true;
        return path.StartsWith(
            parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
    }
}
