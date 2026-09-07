using XDiskInspector.Cleanup;
using XDiskInspector.Core;
using XDiskInspector.Rules;

namespace XDiskInspector.Tests;

public sealed class RuleVersionSafetyTests
{
    [Fact]
    public void Preview_rejects_a_complete_report_from_an_old_rule_version()
    {
        using var fixture = new TempDirectory();
        var file = Path.Combine(fixture.Root, "cache.tmp");
        File.WriteAllText(file, "fixture");
        var matcher = CreateMatcher(fixture.Root, "rules-current");
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
        var staleReport = new ScanReport
        {
            RootPath = fixture.Root,
            RuleVersion = "rules-old",
            IsComplete = true
        };

        var preview = new CleanupPreviewService(matcher, new SafePathPolicy()).Build(staleReport, [item]);

        Assert.False(preview.IsExecutable);
        Assert.Empty(preview.Candidates);
        Assert.Contains(preview.Errors, error => error.Contains("规则版本"));
        Assert.True(File.Exists(file));
    }

    [Fact]
    public async Task Executor_rejects_a_candidate_from_an_old_rule_version_even_if_preview_is_bypassed()
    {
        using var fixture = new TempDirectory();
        var file = Path.Combine(fixture.Root, "cache.tmp");
        File.WriteAllText(file, "fixture");
        var matcher = CreateMatcher(fixture.Root, "rules-current");
        var candidate = new CleanupCandidate(
            file,
            "fixture.cache",
            "rules-old",
            new FileInfo(file).Length,
            File.GetLastWriteTimeUtc(file),
            CleanupKind.File,
            null,
            RiskLevel.Low,
            "fixture consequence",
            false);

        var result = await new CleanupExecutor(matcher, new SafePathPolicy(), new NoopRecycleBinService())
            .ExecuteAsync(
                new CleanupPreview { RuleVersion = "rules-old", Candidates = [candidate] },
                new CleanupExecutionOptions());

        Assert.True(File.Exists(file));
        var item = Assert.Single(result.Items);
        Assert.Equal(CleanupItemStatus.Skipped, item.Status);
        Assert.Contains("规则版本", item.Message);
    }

    [Fact]
    public async Task Recycle_bin_service_rejects_an_empty_scope_without_calling_the_system_api()
    {
        var result = await new ShellRecycleBinService().EmptyAsync(string.Empty);

        Assert.False(result.Success);
    }

    private static PathRuleMatcher CreateMatcher(string root, string version)
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
        return new PathRuleMatcher(new RuleLibrary(version, [rule]));
    }

    private sealed class NoopRecycleBinService : IRecycleBinService
    {
        public Task<(bool Success, string? Error)> EmptyAsync(string rootPath)
            => Task.FromResult<(bool, string?)>((true, null));
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
