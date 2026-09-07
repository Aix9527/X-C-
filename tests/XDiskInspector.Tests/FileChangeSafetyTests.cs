using XDiskInspector.Cleanup;
using XDiskInspector.Core;
using XDiskInspector.Rules;

namespace XDiskInspector.Tests;

public sealed class FileChangeSafetyTests
{
    [Fact]
    public async Task Executor_skips_a_file_whose_size_changed_even_if_timestamp_is_restored()
    {
        using var fixture = new TempDirectory();
        var cacheRoot = Directory.CreateDirectory(Path.Combine(fixture.Root, "Cache"));
        var file = Path.Combine(cacheRoot.FullName, "item.tmp");
        File.WriteAllText(file, "a");
        var scannedTimestamp = File.GetLastWriteTimeUtc(file);
        var scannedSize = new FileInfo(file).Length;

        var matcher = CreateMatcher(cacheRoot.FullName);
        var candidate = new CleanupCandidate(
            file,
            "fixture.cache",
            matcher.RuleVersion,
            scannedSize,
            scannedTimestamp,
            CleanupKind.File,
            null,
            RiskLevel.Low,
            "fixture consequence",
            false);

        File.WriteAllText(file, "content changed after scan");
        File.SetLastWriteTimeUtc(file, scannedTimestamp);

        var result = await new CleanupExecutor(matcher, new SafePathPolicy(), new NoopRecycleBinService())
            .ExecuteAsync(
                new CleanupPreview { RuleVersion = matcher.RuleVersion, Candidates = [candidate] },
                new CleanupExecutionOptions());

        Assert.True(File.Exists(file));
        var item = Assert.Single(result.Items);
        Assert.Equal(CleanupItemStatus.Skipped, item.Status);
        Assert.Contains("大小", item.Message);
        Assert.Contains("发生变化", item.Message);
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
        return new PathRuleMatcher(new RuleLibrary("file-change-tests", [rule]));
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
