using XDiskInspector.Cleanup;
using XDiskInspector.Core;
using XDiskInspector.Rules;

namespace XDiskInspector.Tests;

public sealed class CleanupProgressAndElevationTests
{
    [Fact]
    public async Task Responsive_executor_reports_item_progress_for_confirmed_batch()
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
        var responsive = new ResponsiveCleanupExecutor(
            new CleanupExecutor(matcher, new SafePathPolicy()));

        var result = await responsive.ExecuteAsync(
            preview,
            new CleanupExecutionOptions(),
            progress: new Progress<CleanupProgress>(p => progress.Add(p)));

        Assert.Equal(2, result.RunResult.DeletedCount);
        Assert.Empty(result.RequiresElevation);
        Assert.Contains(progress, p => p.CurrentIndex == 1 && p.TotalCount == 2 && p.CurrentPath == first && p.IsCurrentItemActive);
        Assert.Contains(progress, p => p.CompletedCount == 1 && p.EstimatedCompletedBytes >= 32);
        Assert.Contains(progress, p => p.CompletedCount == 2 && p.EstimatedCompletedBytes >= 96);
    }

    [Fact]
    public async Task Responsive_executor_does_not_block_caller_when_inner_executor_blocks()
    {
        using var gate = new ManualResetEventSlim(false);
        var inner = new BlockingBatchExecutor(gate);
        var responsive = new ResponsiveCleanupExecutor(inner);
        var preview = new CleanupPreview { RuleVersion = "fixture" };
        preview.Candidates.Add(new CleanupCandidate(
            @"C:\fixture\large.bin", "fixture", "fixture", 10L * 1024 * 1024 * 1024,
            DateTime.UtcNow.AddDays(-30), CleanupKind.File, null, RiskLevel.Low, "fixture", false));

        var task = responsive.ExecuteAsync(preview, new CleanupExecutionOptions());

        Assert.False(task.IsCompleted);
        gate.Set();
        await task;
    }

    [Fact]
    public async Task Permission_failure_is_collected_for_elevated_retry()
    {
        var inner = new PermissionDeniedBatchExecutor();
        var responsive = new ResponsiveCleanupExecutor(inner);
        var candidate = new CleanupCandidate(
            @"C:\fixture\protected.tmp", "fixture", "fixture", 16,
            DateTime.UtcNow.AddDays(-30), CleanupKind.File, null, RiskLevel.Low, "fixture", false);
        var preview = new CleanupPreview { RuleVersion = "fixture" };
        preview.Candidates.Add(candidate);

        var result = await responsive.ExecuteAsync(preview, new CleanupExecutionOptions());

        Assert.Single(result.RequiresElevation);
        Assert.Equal(candidate.Path, result.RequiresElevation[0].Path);
        Assert.Equal(CleanupItemStatus.Failed, Assert.Single(result.RunResult.Items).Status);
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

    private sealed class BlockingBatchExecutor(ManualResetEventSlim gate) : ICleanupBatchExecutor
    {
        public Task<CleanupRunResult> ExecuteAsync(CleanupPreview preview, CleanupExecutionOptions options, CancellationToken cancellationToken)
        {
            gate.Wait(TimeSpan.FromSeconds(5));
            return Task.FromResult(new CleanupRunResult
            {
                Items = [new CleanupItemResult(preview.Candidates[0].Path, CleanupItemStatus.Deleted, 0, "ok")]
            });
        }
    }

    private sealed class PermissionDeniedBatchExecutor : ICleanupBatchExecutor
    {
        public Task<CleanupRunResult> ExecuteAsync(CleanupPreview preview, CleanupExecutionOptions options, CancellationToken cancellationToken)
            => Task.FromResult(new CleanupRunResult
            {
                Items = [new CleanupItemResult(preview.Candidates[0].Path, CleanupItemStatus.Failed, 0, "权限不足：fixture denied")]
            });
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
