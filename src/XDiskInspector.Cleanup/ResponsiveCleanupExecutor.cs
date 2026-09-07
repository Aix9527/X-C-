namespace XDiskInspector.Cleanup;

public sealed record CleanupProgress(
    int CurrentIndex,
    int TotalCount,
    string CurrentPath,
    int CompletedCount,
    long EstimatedCompletedBytes,
    long EstimatedTotalBytes,
    bool IsCurrentItemActive)
{
    public double Percent => EstimatedTotalBytes <= 0
        ? (TotalCount <= 0 ? 0 : Math.Clamp((double)CompletedCount / TotalCount * 100d, 0d, 100d))
        : Math.Clamp((double)EstimatedCompletedBytes / EstimatedTotalBytes * 100d, 0d, 100d);
}

public interface ICleanupBatchExecutor
{
    Task<CleanupRunResult> ExecuteAsync(
        CleanupPreview preview,
        CleanupExecutionOptions options,
        CancellationToken cancellationToken);
}

internal sealed class CleanupBatchExecutorAdapter(CleanupExecutor inner) : ICleanupBatchExecutor
{
    public Task<CleanupRunResult> ExecuteAsync(
        CleanupPreview preview,
        CleanupExecutionOptions options,
        CancellationToken cancellationToken)
        => inner.ExecuteAsync(preview, options, cancellationToken);
}

public sealed class ResponsiveCleanupRunResult
{
    public CleanupRunResult RunResult { get; } = new();
    public List<CleanupCandidate> RequiresElevation { get; } = [];
}

public sealed class ResponsiveCleanupExecutor
{
    private readonly ICleanupBatchExecutor _inner;

    public ResponsiveCleanupExecutor(CleanupExecutor inner)
        : this(new CleanupBatchExecutorAdapter(inner))
    {
    }

    public ResponsiveCleanupExecutor(ICleanupBatchExecutor inner)
        => _inner = inner;

    public async Task<ResponsiveCleanupRunResult> ExecuteAsync(
        CleanupPreview preview,
        CleanupExecutionOptions options,
        CancellationToken stopAfterCurrentItem = default,
        IProgress<CleanupProgress>? progress = null)
    {
        var aggregate = new ResponsiveCleanupRunResult();
        if (!preview.IsExecutable)
        {
            foreach (var error in preview.Errors)
                aggregate.RunResult.Items.Add(new CleanupItemResult("(预览)", CleanupItemStatus.Skipped, 0, error));
            return aggregate;
        }

        var totalCount = preview.Candidates.Count;
        var totalBytes = preview.Candidates.Sum(x => Math.Max(0, x.EstimatedSizeBytes));
        var completedCount = 0;
        long completedBytes = 0;

        for (var index = 0; index < totalCount; index++)
        {
            if (stopAfterCurrentItem.IsCancellationRequested)
            {
                aggregate.RunResult.Stopped = true;
                break;
            }

            var candidate = preview.Candidates[index];
            progress?.Report(new CleanupProgress(
                index + 1,
                totalCount,
                candidate.Path,
                completedCount,
                completedBytes,
                totalBytes,
                true));

            var singlePreview = new CleanupPreview { RuleVersion = preview.RuleVersion };
            singlePreview.Candidates.Add(candidate);

            // CleanupExecutor contains synchronous filesystem calls such as File.Delete.
            // Running each confirmed item on the thread pool keeps the WPF dispatcher responsive,
            // especially for very large files or slow filesystem/filter-driver operations.
            var singleResult = await Task.Run(
                () => _inner.ExecuteAsync(singlePreview, options, CancellationToken.None),
                CancellationToken.None).ConfigureAwait(false);

            foreach (var item in singleResult.Items)
            {
                aggregate.RunResult.Items.Add(item);
                if (item.Status == CleanupItemStatus.Failed && IsPermissionFailure(item.Message))
                    aggregate.RequiresElevation.Add(candidate);
            }

            completedCount++;
            completedBytes += Math.Max(0, candidate.EstimatedSizeBytes);
            progress?.Report(new CleanupProgress(
                index + 1,
                totalCount,
                candidate.Path,
                completedCount,
                completedBytes,
                totalBytes,
                false));
        }

        return aggregate;
    }

    private static bool IsPermissionFailure(string? message)
        => !string.IsNullOrWhiteSpace(message) &&
           (message.StartsWith("权限不足", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Access is denied", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase));
}
