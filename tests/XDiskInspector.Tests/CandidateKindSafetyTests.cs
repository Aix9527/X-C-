using XDiskInspector.Cleanup;
using XDiskInspector.Core;
using XDiskInspector.Rules;

namespace XDiskInspector.Tests;

public sealed class CandidateKindSafetyTests
{
    [Fact]
    public void File_cleanup_rule_rejects_a_directory_item_even_if_report_marks_it_selectable()
    {
        using var fixture = new TempDirectory();
        var cache = Directory.CreateDirectory(Path.Combine(fixture.Root, "cache"));
        var matcher = CreateMatcher(cache.FullName, CleanupKind.File);
        var classification = matcher.Classify(cache.FullName)!;
        var item = new ScanItem
        {
            Path = cache.FullName,
            Name = cache.Name,
            Kind = ScanItemKind.Directory,
            Software = classification.Software,
            Purpose = classification.Purpose,
            Consequence = classification.Consequence,
            RiskLevel = classification.RiskLevel,
            Recommendation = classification.Recommendation,
            CleanupRuleId = classification.RuleId,
            CleanupKind = CleanupKind.File,
            Selectable = true
        };
        item.Selected = true;
        var report = new ScanReport { RootPath = fixture.Root, RuleVersion = matcher.RuleVersion, IsComplete = true };

        var preview = new CleanupPreviewService(matcher, new SafePathPolicy()).Build(report, [item]);

        Assert.False(preview.IsExecutable);
        Assert.Contains(preview.Errors, x => x.Contains("文件清理规则"));
    }

    [Fact]
    public void Recycle_bin_rule_rejects_a_normal_file_item_even_if_report_marks_it_selectable()
    {
        using var fixture = new TempDirectory();
        var recycle = Directory.CreateDirectory(Path.Combine(fixture.Root, "recycle"));
        var file = Path.Combine(recycle.FullName, "item.bin");
        File.WriteAllText(file, "fixture");
        var matcher = CreateMatcher(recycle.FullName, CleanupKind.RecycleBin);
        var classification = matcher.Classify(file)!;
        var item = new ScanItem
        {
            Path = file,
            Name = Path.GetFileName(file),
            Kind = ScanItemKind.File,
            SizeBytes = new FileInfo(file).Length,
            Software = classification.Software,
            Purpose = classification.Purpose,
            Consequence = classification.Consequence,
            RiskLevel = classification.RiskLevel,
            Recommendation = classification.Recommendation,
            CleanupRuleId = classification.RuleId,
            CleanupKind = CleanupKind.RecycleBin,
            Selectable = true,
            Irreversible = true
        };
        item.Selected = true;
        var report = new ScanReport { RootPath = fixture.Root, RuleVersion = matcher.RuleVersion, IsComplete = true };

        var preview = new CleanupPreviewService(matcher, new SafePathPolicy()).Build(report, [item]);

        Assert.False(preview.IsExecutable);
        Assert.Contains(preview.Errors, x => x.Contains("回收站规则"));
    }

    private static PathRuleMatcher CreateMatcher(string root, CleanupKind kind)
    {
        var rule = new DirectoryRule
        {
            Id = kind == CleanupKind.File ? "fixture.file" : "fixture.recycle",
            PathPattern = root,
            Software = "Fixture",
            Purpose = "Fixture",
            Consequence = "Fixture",
            RiskLevel = RiskLevel.Medium,
            Recommendation = CleanupRecommendation.Confirm,
            AllowCleanup = true,
            CleanupKind = kind,
            AppliesToDescendants = true,
            Irreversible = kind == CleanupKind.RecycleBin
        };
        return new PathRuleMatcher(new RuleLibrary("candidate-kind-tests", [rule]));
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Root { get; } = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "XDiskInspector.Tests", Guid.NewGuid().ToString("N"))).FullName;

        public void Dispose()
        {
            try { Directory.Delete(Root, true); } catch { }
        }
    }
}
