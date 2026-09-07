using System.Diagnostics;
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

        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "XDiskInspector",
            "Elevation");
        Directory.CreateDirectory(root);

        var token = Guid.NewGuid().ToString("N");
        var requestPath = Path.Combine(root, token + ".request.json");
        var responsePath = Path.Combine(root, token + ".response.json");
        var progressPath = Path.Combine(root, token + ".progress.json");

        try
        {
            var request = new ElevatedCleanupRequest(candidates.ToList(), options.AllowRecycleBinIrreversible);
            await File.WriteAllTextAsync(requestPath, JsonSerializer.Serialize(request, JsonOptions));

            var arguments = $"--elevated-cleanup {Quote(requestPath)} {Quote(responsePath)} {Quote(progressPath)}";
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
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                return new ElevatedCleanupBridgeResult(false, null, "用户取消了管理员权限请求。" );
            }
            catch (Exception ex)
            {
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
                    return new ElevatedCleanupBridgeResult(true, null, $"管理员清理进程退出码：{process.ExitCode}" );
            }

            if (!File.Exists(responsePath))
                return new ElevatedCleanupBridgeResult(true, null, "管理员清理没有返回结果文件。" );

            var json = await File.ReadAllTextAsync(responsePath);
            var result = JsonSerializer.Deserialize<CleanupRunResult>(json, JsonOptions);
            return result is null
                ? new ElevatedCleanupBridgeResult(true, null, "管理员清理返回结果无效。" )
                : new ElevatedCleanupBridgeResult(true, result, null);
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
        if (!IsElevatedCleanupInvocation(args)) return 2;
        if (!IsAdministrator()) return 5;

        var requestPath = args[2];
        var responsePath = args[3];
        var progressPath = args.Length >= 5 ? args[4] : string.Empty;

        try
        {
            var requestJson = File.ReadAllText(requestPath);
            var request = JsonSerializer.Deserialize<ElevatedCleanupRequest>(requestJson, JsonOptions);
            if (request is null || request.Candidates.Count == 0) return 3;

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
            return 0;
        }
        catch
        {
            return 4;
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
