using XDiskInspector.Cleanup;
using XDiskInspector.Core;
using XDiskInspector.Rules;

namespace XDiskInspector.Tests;

public sealed class RecycleBinScopeTests
{
    [Fact]
    public async Task Scanner_carries_recognized_recycle_bin_content_size_into_special_candidate()
    {
        using var fixture = new TempDirectory();
        await File.WriteAllBytesAsync(Path.Combine(fixture.Root, "a.bin"), new byte[11]);
        var nested = Directory.CreateDirectory(Path.Combine(fixture.Root, "nested"));
        await File.WriteAllBytesAsync(Path.Combine(nested.FullName, "b.bin"), new byte[19]);

        var matcher = CreateRecycleMatcher(fixture.Root);
        var scanner = new FileSystemScanner(matcher);
        var report = await scanner.ScanAsync(new ScanOptions
        {
            RootPath = fixture.Root,
            LargeFileThresholdBytes = long.MaxValue
        });

        var candidate = Assert.Single(report.CleanupCandidates);
        Assert.Equal(CleanupKind.RecycleBin, candidate.CleanupKind);
        Assert.Equal(30, candidate.SizeBytes);
        Assert.False(candidate.Selected);
    }

    [Fact]
    public async Task Executor_passes_only_the_candidate_drive_root_to_recycle_bin_service()
    {
        using var fixture = new TempDirectory();
        var matcher = CreateRecycleMatcher(fixture.Root);
        var fake = new CapturingRecycleBinService();
        var executor = new CleanupExecutor(matcher, new SafePathPolicy(), fake);
        var candidate = new CleanupCandidate(
            fixture.Root,
            "fixture.recycle",
            matcher.RuleVersion,
            0,
            null,
            CleanupKind.RecycleBin,
            null,
            RiskLevel.Medium,
            "fixture recycle",
            true);

        var result = await executor.ExecuteAsync(
            new CleanupPreview { RuleVersion = matcher.RuleVersion, Candidates = [candidate] },
            new CleanupExecutionOptions(true));

        Assert.Equal(CleanupItemStatus.Deleted, Assert.Single(result.Items).Status);
        Assert.Equal(1, fake.CallCount);
        Assert.Equal(Path.GetPathRoot(fixture.Root), fake.LastRootPath, ignoreCase: true);
    }

    private static PathRuleMatcher CreateRecycleMatcher(string root)
    {
        var rule = new DirectoryRule
        {
            Id = "fixture.recycle",
            PathPattern = root,
            Software = "Fixture Recycle Bin",
            Purpose = "Test recycle-bin scope",
            Consequence = "fixture recycle",
            RiskLevel = RiskLevel.Medium,
            Recommendation = CleanupRecommendation.Confirm,
            AllowCleanup = true,
            CleanupKind = CleanupKind.RecycleBin,
            AppliesToDescendants = true,
            Irreversible = true
        };
        return new PathRuleMatcher(new RuleLibrary("recycle-scope-tests", [rule]));
    }

    private sealed class CapturingRecycleBinService : IRecycleBinService
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
