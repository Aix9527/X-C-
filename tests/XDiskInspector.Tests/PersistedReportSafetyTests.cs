using XDiskInspector.Cleanup;
using XDiskInspector.Core;
using XDiskInspector.Reporting;
using XDiskInspector.Rules;

namespace XDiskInspector.Tests;

public sealed class PersistedReportSafetyTests
{
    [Fact]
    public async Task Json_round_trip_marks_report_as_persisted_and_preview_refuses_cleanup()
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
            CleanupRuleId = classification.RuleId,
            CleanupKind = classification.CleanupKind,
            Selectable = true,
            LastWriteTimeUtc = File.GetLastWriteTimeUtc(file)
        };
        item.Selected = true;

        var liveReport = new ScanReport
        {
            RootPath = fixture.Root,
            RuleVersion = matcher.RuleVersion,
            IsComplete = true,
            CleanupCandidates = [item]
        };
        Assert.False(ScanReportRuntimeState.IsPersisted(liveReport));

        var reportPath = Path.Combine(fixture.Root, "report.json");
        var writer = new JsonReportWriter();
        await writer.WriteAsync(liveReport, reportPath);
        Assert.False(ScanReportRuntimeState.IsPersisted(liveReport));

        var json = await File.ReadAllTextAsync(reportPath);
        Assert.DoesNotContain("Persisted", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RuntimeState", json, StringComparison.OrdinalIgnoreCase);

        var loaded = await writer.ReadAsync(reportPath);
        Assert.True(loaded.IsComplete);
        Assert.Equal(matcher.RuleVersion, loaded.RuleVersion);
        Assert.True(ScanReportRuntimeState.IsPersisted(loaded));

        var preview = new CleanupPreviewService(matcher, new SafePathPolicy())
            .Build(loaded, loaded.CleanupCandidates);

        Assert.False(preview.IsExecutable);
        Assert.Empty(preview.Candidates);
        Assert.Contains(preview.Errors, error => error.Contains("历史报告") && error.Contains("重新扫描"));
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
