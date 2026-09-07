using XDiskInspector.Cleanup;
using XDiskInspector.Core;
using XDiskInspector.Rules;

namespace XDiskInspector.Tests;

public sealed class TamperResistanceTests
{
    [Fact]
    public void Preview_uses_current_rule_security_fields_instead_of_tampered_report_values()
    {
        using var fixture = new TempDirectory();
        var root = Directory.CreateDirectory(Path.Combine(fixture.Root, "Temp"));
        var file = Path.Combine(root.FullName, "fresh.tmp");
        File.WriteAllText(file, "fresh");
        var matcher = CreateMatcher(root.FullName, minAgeDays: 7);
        var classification = matcher.Classify(file)!;
        var tampered = new ScanItem
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
            LastWriteTimeUtc = File.GetLastWriteTimeUtc(file),
            MinAgeDays = null,
            Irreversible = true
        };
        tampered.Selected = true;
        var report = new ScanReport { RootPath = fixture.Root, RuleVersion = matcher.RuleVersion, IsComplete = true };

        var preview = new CleanupPreviewService(matcher, new SafePathPolicy()).Build(report, [tampered]);

        Assert.True(preview.IsExecutable);
        var candidate = Assert.Single(preview.Candidates);
        Assert.Equal(CleanupKind.File, candidate.CleanupKind);
        Assert.Equal(7, candidate.MinAgeDays);
        Assert.False(candidate.Irreversible);
    }

    [Fact]
    public async Task Executor_rejects_tampered_cleanup_kind_before_any_irreversible_action()
    {
        using var fixture = new TempDirectory();
        var root = Directory.CreateDirectory(Path.Combine(fixture.Root, "Cache"));
        var file = Path.Combine(root.FullName, "data.tmp");
        File.WriteAllText(file, "data");
        var matcher = CreateMatcher(root.FullName, minAgeDays: null);
        var recycle = new CountingRecycleBinService();
        var executor = new CleanupExecutor(matcher, new SafePathPolicy(), recycle);
        var candidate = new CleanupCandidate(
            file, "fixture.cache", matcher.RuleVersion, new FileInfo(file).Length,
            File.GetLastWriteTimeUtc(file), CleanupKind.RecycleBin, null,
            RiskLevel.Low, "Fixture data is deleted", true);
        var preview = new CleanupPreview { RuleVersion = matcher.RuleVersion, Candidates = [candidate] };

        var result = await executor.ExecuteAsync(preview, new CleanupExecutionOptions(true));

        Assert.True(File.Exists(file));
        Assert.Equal(0, recycle.CallCount);
        Assert.Equal(CleanupItemStatus.Skipped, Assert.Single(result.Items).Status);
        Assert.Contains("规则", result.Items.Single().Message);
    }

    [Fact]
    public void Safe_path_policy_rejects_a_target_beneath_a_reparse_point_ancestor_when_supported()
    {
        using var fixture = new TempDirectory();
        var allowedRoot = Directory.CreateDirectory(Path.Combine(fixture.Root, "allowed"));
        var outside = Directory.CreateDirectory(Path.Combine(fixture.Root, "outside"));
        var outsideFile = Path.Combine(outside.FullName, "outside.tmp");
        File.WriteAllText(outsideFile, "outside");
        var link = Path.Combine(allowedRoot.FullName, "link");
        try { Directory.CreateSymbolicLink(link, outside.FullName); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException) { return; }

        var result = new SafePathPolicy().Validate(Path.Combine(link, "outside.tmp"));

        Assert.False(result.IsSafe);
        Assert.Contains("重解析点", result.Reason ?? string.Empty);
    }

    private static PathRuleMatcher CreateMatcher(string root, int? minAgeDays)
    {
        var rule = new DirectoryRule
        {
            Id = "fixture.cache",
            PathPattern = root,
            Software = "Fixture Cache",
            Purpose = "Test fixture cache",
            Consequence = "Fixture data is deleted",
            RiskLevel = RiskLevel.Low,
            Recommendation = CleanupRecommendation.Suggested,
            AllowCleanup = true,
            CleanupKind = CleanupKind.File,
            MinAgeDays = minAgeDays,
            AppliesToDescendants = true
        };
        return new PathRuleMatcher(new RuleLibrary("tamper-tests", [rule]));
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

    private sealed class CountingRecycleBinService : IRecycleBinService
    {
        public int CallCount { get; private set; }
        public string? LastRootPath { get; private set; }

        public Task<(bool Success, string? Error)> EmptyAsync(string rootPath)
        {
            CallCount++;
            LastRootPath = rootPath;
            return Task.FromResult<(bool, string?)>((true, null));
        }
    }
}
