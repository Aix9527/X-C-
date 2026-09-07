using XDiskInspector.Cleanup;
using XDiskInspector.Core;
using XDiskInspector.Reporting;
using XDiskInspector.Rules;

namespace XDiskInspector.Tests;

public sealed class PersistedReportSafetyTests
{
    [Fact]
    public async Task Json_round_trip_marks_report_as_persisted_clears_selection_and_allows_live_revalidated_preview()
    {
        using var fixture = new TempDirectory();
        var cacheRoot = Directory.CreateDirectory(Path.Combine(fixture.Root, "Cache"));
        var file = Path.Combine(cacheRoot.FullName, "old.tmp");
        File.WriteAllText(file, "fixture");

        var matcher = CreateMatcher(cacheRoot.FullName);
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
            CleanupRuleId = "tampered.old-rule",
            CleanupKind = classification.CleanupKind,
            Selectable = true,
            LastWriteTimeUtc = File.GetLastWriteTimeUtc(file)
        };
        item.Selected = true;

        var liveReport = new ScanReport
        {
            RootPath = fixture.Root,
            RuleVersion = "old-report-version",
            IsComplete = true,
            CleanupCandidates = [item]
        };

        var reportPath = Path.Combine(fixture.Root, "report.json");
        var writer = new JsonReportWriter();
        await writer.WriteAsync(liveReport, reportPath);

        var loaded = await writer.ReadAsync(reportPath);
        Assert.True(ScanReportRuntimeState.IsPersisted(loaded));
        Assert.All(loaded.CleanupCandidates, candidate => Assert.False(candidate.Selected));

        var loadedItem = Assert.Single(loaded.CleanupCandidates);
        loadedItem.Selected = true;
        Assert.True(loadedItem.Selected);

        var preview = new CleanupPreviewService(matcher, new SafePathPolicy())
            .Build(loaded, loaded.CleanupCandidates);

        Assert.True(preview.IsExecutable);
        Assert.Empty(preview.Errors);
        var candidate = Assert.Single(preview.Candidates);
        Assert.Equal("fixture.cache", candidate.RuleId);
        Assert.Equal(matcher.RuleVersion, candidate.RuleVersion);
        Assert.Equal(new FileInfo(file).Length, candidate.EstimatedSizeBytes);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public async Task Persisted_report_revalidation_rejects_target_that_current_rules_no_longer_allow()
    {
        using var fixture = new TempDirectory();
        var cacheRoot = Directory.CreateDirectory(Path.Combine(fixture.Root, "Cache"));
        var file = Path.Combine(cacheRoot.FullName, "old.tmp");
        File.WriteAllText(file, "fixture");

        var permissiveMatcher = CreateMatcher(cacheRoot.FullName);
        var classification = permissiveMatcher.Classify(file)!;
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
            CleanupKind = classification.CleanupKind,
            Selectable = true,
            LastWriteTimeUtc = File.GetLastWriteTimeUtc(file)
        };

        var report = new ScanReport
        {
            RootPath = fixture.Root,
            RuleVersion = permissiveMatcher.RuleVersion,
            IsComplete = true,
            CleanupCandidates = [item]
        };

        var reportPath = Path.Combine(fixture.Root, "report.json");
        var writer = new JsonReportWriter();
        await writer.WriteAsync(report, reportPath);
        var loaded = await writer.ReadAsync(reportPath);
        loaded.CleanupCandidates[0].Selected = true;

        var denyRule = new DirectoryRule
        {
            Id = "fixture.cache",
            PathPattern = cacheRoot.FullName,
            Software = "Fixture Cache",
            Purpose = "fixture purpose",
            Consequence = "fixture consequence",
            RiskLevel = RiskLevel.High,
            Recommendation = CleanupRecommendation.Keep,
            AllowCleanup = false,
            CleanupKind = CleanupKind.None,
            AppliesToDescendants = true
        };
        var currentMatcher = new PathRuleMatcher(new RuleLibrary("current-deny", [denyRule]));

        var preview = new CleanupPreviewService(currentMatcher, new SafePathPolicy())
            .Build(loaded, loaded.CleanupCandidates);

        Assert.False(preview.IsExecutable);
        Assert.Empty(preview.Candidates);
        Assert.Contains(preview.Errors, error => error.Contains("当前规则库") || error.Contains("不再允许"));
        Assert.True(File.Exists(file));
    }

    private static PathRuleMatcher CreateMatcher(string root)
    {
        var rule = new DirectoryRule
        {
            Id = "fixture.cache",
            PathPattern = root,
            Software = "Fixture Cache",
            Purpose = "fixture purpose",
            Consequence = "fixture consequence",
            RiskLevel = RiskLevel.Low,
            Recommendation = CleanupRecommendation.Suggested,
            AllowCleanup = true,
            CleanupKind = CleanupKind.File,
            AppliesToDescendants = true
        };
        return new PathRuleMatcher(new RuleLibrary("persisted-report-tests", [rule]));
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
