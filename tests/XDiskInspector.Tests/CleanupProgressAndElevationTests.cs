using XDiskInspector.Cleanup;
using XDiskInspector.Core;
using XDiskInspector.Rules;

namespace XDiskInspector.Tests;

public sealed class CleanupProgressAndElevationTests
{
    [Fact]
    public async Task Executor_reports_item_progress_for_confirmed_batch()
    {
        using var fixture = new TempDirectory();
        var cacheRoot = Directory.CreateDirectory(Path.Combine(fixture.Root, "Cache"));
        var first = CreateFile(cacheRoot.FullName, "a.tmp", 32);
        var second = CreateFile(cacheRoot.FullName, "b.tmp", 64);
        var matcher = CreateMatcher(cacheRoot.FullName);
        var preview = new CleanupPreview { RuleVersion = matcher.RuleVersion };
        preview.Candidates.Add(CreateCandidate(matcher, first));
        preview.Candidates.Add(CreateCandidate(matcher, second));

        var progress = new List<CleanupProgress>();
        var executor = new CleanupExecutor(
            matcher,
            new SafePathPolicy(),
            fileDeleteBackend: new BackgroundFileDeleteBackend());

        var result = await executor.ExecuteAsync(
            preview,
            new CleanupExecutionOptions(),
            progress: new Progress<CleanupProgress>(p => progress.Add(p)));

        Assert.Equal(2, result.DeletedCount);
        Assert.Contains(progress, p => p.CurrentIndex == 1 && p.TotalCount == 2 && p.CurrentPath == first && p.IsCurrentItemActive);
        Assert.Contains(progress, p => p.CompletedCount == 1 && p.EstimatedCompletedBytes >= 32);
        Assert.Contains(progress, p => p.CompletedCount == 2 && p.EstimatedCompletedBytes >= 96);
    }

    [Fact]
    public async Task Background_delete_backend_returns_without_blocking_calling_thread()
    {
        using var gate = new ManualResetEventSlim(false);
        var backend = new BackgroundFileDeleteBackend(_ => gate.Wait(TimeSpan.FromSeconds(5)));

        var task = backend.DeleteAsync(@"C:\fixture\large.bin", CancellationToken.None);

        Assert.False(task.IsCompleted);
        gate.Set();
        await task;
    }

    [Fact]
    public async Task Unauthorized_delete_is_reported_as_requires_elevation()
    {
        using var fixture = new TempDirectory();
        var cacheRoot = Directory.CreateDirectory(Path.Combine(fixture.Root, "Cache"));
        var file = CreateFile(cacheRoot.FullName, "protected.tmp", 16);
        var matcher = CreateMatcher(cacheRoot.FullName);
        var preview = new CleanupPreview { RuleVersion = matcher.RuleVersion };
        preview.Candidates.Add(CreateCandidate(matcher, file));

        var executor = new CleanupExecutor(
            matcher,
            new SafePathPolicy(),
            fileDeleteBackend: new ThrowingDeleteBackend(new UnauthorizedAccessException("fixture denied")));

        var result = await executor.ExecuteAsync(preview, new CleanupExecutionOptions());

        var item = Assert.Single(result.Items);
        Assert.Equal(CleanupItemStatus.RequiresElevation, item.Status);
        Assert.True(File.Exists(file));
    }

    private static string CreateFile(string root, string name, int size)
    {
        var path = Path.Combine(root, name);
        File.WriteAllBytes(path, new byte[size]);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-30));
        return path;
    }

    private static CleanupCandidate CreateCandidate(PathRuleMatcher matcher, string path)
    {
        var classification = matcher.Classify(path)!;
        var info = new FileInfo(path);
        return new CleanupCandidate(
            path,
            classification.RuleId,
            matcher.RuleVersion,
            info.Length,
            info.LastWriteTimeUtc,
            classification.CleanupKind,
            classification.MinAgeDays,
            classification.RiskLevel,
            classification.Consequence,
            classification.Irreversible);
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
        return new PathRuleMatcher(new RuleLibrary("cleanup-progress-tests", [rule]));
    }

    private sealed class ThrowingDeleteBackend(Exception exception) : IFileDeleteBackend
    {
        public Task DeleteAsync(string path, CancellationToken cancellationToken)
            => Task.FromException(exception);
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
