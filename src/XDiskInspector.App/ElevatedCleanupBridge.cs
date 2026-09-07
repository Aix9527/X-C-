using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text.Json;
using XDiskInspector.Cleanup;
using XDiskInspector.Rules;

namespace XDiskInspector.App;

internal sealed record ElevatedCleanupRequest(
    List<CleanupCandidate> Candidates,
    bool AllowRecycleBinIrreversible);

internal sealed record ElevatedCleanupProgressSnapshot(
    int CurrentIndex,
    int TotalCount,
    string CurrentPath,
    int CompletedCount,
    long EstimatedCompletedBytes,
    long EstimatedTotalBytes,
    bool IsCurrentItemActive,
    double Percent);

internal sealed record ElevatedCleanupBridgeResult(
    bool Started,
    CleanupRunResult? Result,
    string? Error);

internal sealed class ElevatedCleanupBridge
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true
    };

    public async Task<ElevatedCleanupBridgeResult> RunAsync(
        IReadOnlyList<CleanupCandidate> candidates,
        CleanupExecutionOptions options,
        IProgress<CleanupProgress>? progress = null)
    {
        if (!OperatingSystem.IsWindows())
            return new ElevatedCleanupBridgeResult(false, null, "管理员清理仅支持 Windows。" );
        if (candidates.Count == 0)
            return new ElevatedCleanupBridgeResult(false, null, "没有需要管理员权限重试的项目。" );

        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            return new ElevatedCleanupBridgeResult(false, null, "无法定位当前程序可执行文件。" );

        try
        {
            ElevationStorage.EnsureAccessible();
        }
        catch (Exception ex)
        {
            ElevationStorage.AppendLog($"Bridge: failed to initialize elevation directory: {ex.Message}");
            return new ElevatedCleanupBridgeResult(false, null, $"无法初始化管理员清理目录：{ex.Message}");
        }
        var root = ElevationStorage.RootDirectory;
        ElevationStorage.AppendLog($"Bridge: elevation directory ready at {root}");

        var token = Guid.NewGuid().ToString("N");
        var requestPath = Path.Combine(root, token + ".request.json");
        var responsePath = Path.Combine(root, token + ".response.json");
        var progressPath = Path.Combine(root, token + ".progress.json");

        try
        {
            var request = new ElevatedCleanupRequest(candidates.ToList(), options.AllowRecycleBinIrreversible);
            await File.WriteAllTextAsync(requestPath, JsonSerializer.Serialize(request, JsonOptions));
            ElevationStorage.AppendLog($"Bridge: request created: {Path.GetFileName(requestPath)} ({candidates.Count} items)");

            if (!File.Exists(requestPath))
            {
                ElevationStorage.AppendLog("Bridge: request file missing right after write; aborting elevation.");
                return new ElevatedCleanupBridgeResult(false, null, "清理请求文件未能落盘，已中止管理员清理。");
            }

            var arguments = $"--elevated-cleanup {Quote(requestPath)} {Quote(responsePath)} {Quote(progressPath)}";
            ElevationStorage.AppendLog("Bridge: launching elevated worker (runas)...");
            Process? process;
            try
            {
                process = Process.Start(new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = arguments,
                    UseShellExecute = true,
                    Verb = "runas",
                    WorkingDirectory = AppContext.BaseDirectory
                });
                ElevationStorage.AppendLog("Bridge: elevated worker process started.");
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                ElevationStorage.AppendLog("Bridge: user cancelled the UAC consent prompt.");
                return new ElevatedCleanupBridgeResult(false, null, "用户取消了管理员权限请求。" );
            }
            catch (Exception ex)
            {
                ElevationStorage.AppendLog($"Bridge: failed to start elevated worker: {ex.Message}");
                return new ElevatedCleanupBridgeResult(false, null, $"无法启动管理员清理进程：{ex.Message}" );
            }

            if (process is null)
                return new ElevatedCleanupBridgeResult(false, null, "管理员清理进程未能启动。" );

            using (process)
            {
                var waitTask = process.WaitForExitAsync();
                while (!waitTask.IsCompleted)
                {
                    TryReportProgress(progressPath, progress);
                    await Task.WhenAny(waitTask, Task.Delay(250));
                }
                await waitTask;
                TryReportProgress(progressPath, progress);

                if (process.ExitCode != 0 && !File.Exists(responsePath))
                {
                    ElevationStorage.AppendLog($"Bridge: elevated worker exited with code {process.ExitCode}.");
                    return new ElevatedCleanupBridgeResult(true, null,
                        $"管理员清理进程退出码：{process.ExitCode}（{ElevationErrorCodes.Describe(process.ExitCode)}）");
                }
            }

            if (!File.Exists(responsePath))
                return new ElevatedCleanupBridgeResult(true, null, "管理员清理没有返回结果文件。" );

            var json = await File.ReadAllTextAsync(responsePath);
            var result = JsonSerializer.Deserialize<CleanupRunResult>(json, JsonOptions);
            if (result is not null)
            {
                ElevationStorage.AppendLog($"Bridge: elevated cleanup finished: {result.DeletedCount} deleted, {result.FailedCount} failed, {result.SkippedCount} skipped.");
                return new ElevatedCleanupBridgeResult(true, result, null);
            }
            return new ElevatedCleanupBridgeResult(true, null, "管理员清理返回结果无效。" );
        }
        finally
        {
            TryDelete(requestPath);
            TryDelete(responsePath);
            TryDelete(progressPath);
        }
    }

    private static void TryReportProgress(string progressPath, IProgress<CleanupProgress>? progress)
    {
        if (progress is null || !File.Exists(progressPath)) return;
        try
        {
            var json = File.ReadAllText(progressPath);
            var snapshot = JsonSerializer.Deserialize<ElevatedCleanupProgressSnapshot>(json, JsonOptions);
            if (snapshot is null) return;
            progress.Report(new CleanupProgress(
                snapshot.CurrentIndex,
                snapshot.TotalCount,
                snapshot.CurrentPath,
                snapshot.CompletedCount,
                snapshot.EstimatedCompletedBytes,
                snapshot.EstimatedTotalBytes,
                snapshot.IsCurrentItemActive));
        }
        catch
        {
            // Progress is best-effort. The worker result remains authoritative.
        }
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}

internal static class ElevatedCleanupWorker
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true
    };

    public static bool IsElevatedCleanupInvocation(string[] args)
        => args.Length >= 4 && string.Equals(args[1], "--elevated-cleanup", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (!IsElevatedCleanupInvocation(args))
        {
            ElevationStorage.AppendLog("Worker: invalid invocation arguments.");
            return ElevationErrorCodes.BadInvocation;
        }
        if (!IsAdministrator())
        {
            ElevationStorage.AppendLog("Worker: process is not running as administrator.");
            return ElevationErrorCodes.NotElevated;
        }

        var requestPath = args[2];
        var responsePath = args[3];
        var progressPath = args.Length >= 5 ? args[4] : string.Empty;

        string requestJson;
        try
        {
            requestJson = File.ReadAllText(requestPath);
        }
        catch (FileNotFoundException)
        {
            ElevationStorage.AppendLog($"Worker: request file not found: {requestPath}");
            return ElevationErrorCodes.RequestNotFound;
        }
        catch (UnauthorizedAccessException)
        {
            ElevationStorage.AppendLog($"Worker: request file access denied: {requestPath}");
            return ElevationErrorCodes.RequestAccessDenied;
        }
        catch (IOException)
        {
            ElevationStorage.AppendLog($"Worker: request file locked or still being written: {requestPath}");
            return ElevationErrorCodes.RequestLocked;
        }
        ElevationStorage.AppendLog($"Worker: request loaded: {Path.GetFileName(requestPath)}");

        try
        {
            var request = JsonSerializer.Deserialize<ElevatedCleanupRequest>(requestJson, JsonOptions);
            if (request is null || request.Candidates.Count == 0)
            {
                ElevationStorage.AppendLog("Worker: request content invalid or empty.");
                return ElevationErrorCodes.RequestInvalid;
            }

            var library = RuleLibrary.LoadDefault();
            var matcher = new PathRuleMatcher(library);
            var executor = new CleanupExecutor(matcher, new SafePathPolicy());
            var responsive = new ResponsiveCleanupExecutor(executor);
            var preview = new CleanupPreview { RuleVersion = matcher.RuleVersion };
            preview.Candidates.AddRange(request.Candidates);

            var progress = string.IsNullOrWhiteSpace(progressPath)
                ? null
                : new Progress<CleanupProgress>(p => WriteProgress(progressPath, p));

            var run = responsive.ExecuteAsync(
                    preview,
                    new CleanupExecutionOptions(request.AllowRecycleBinIrreversible),
                    progress: progress)
                .GetAwaiter()
                .GetResult();

            Directory.CreateDirectory(Path.GetDirectoryName(responsePath)!);
            File.WriteAllText(responsePath, JsonSerializer.Serialize(run.RunResult, JsonOptions));
            ElevationStorage.AppendLog($"Worker: cleanup finished: {run.RunResult.DeletedCount} deleted, {run.RunResult.FailedCount} failed, {run.RunResult.SkippedCount} skipped.");
            return ElevationErrorCodes.Success;
        }
        catch
        {
            ElevationStorage.AppendLog("Worker: unexpected failure during elevated cleanup.");
            return ElevationErrorCodes.UnexpectedFailure;
        }
    }

    private static void WriteProgress(string path, CleanupProgress progress)
    {
        try
        {
            var snapshot = new ElevatedCleanupProgressSnapshot(
                progress.CurrentIndex,
                progress.TotalCount,
                progress.CurrentPath,
                progress.CompletedCount,
                progress.EstimatedCompletedBytes,
                progress.EstimatedTotalBytes,
                progress.IsCurrentItemActive,
                progress.Percent);
            File.WriteAllText(path, JsonSerializer.Serialize(snapshot, JsonOptions));
        }
        catch
        {
            // Parent can continue without intermediate progress.
        }
    }

    private static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}
