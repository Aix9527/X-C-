using System.Reflection;
using XDiskInspector.Cleanup;
using XDiskInspector.Core;
using XDiskInspector.Reporting;
using XDiskInspector.Rules;

namespace XDiskInspector.Tests;

public sealed class SafetyAndBehaviorTests
{
    [Fact]
    public void Selection_state_never_selects_a_disabled_item()
    {
        var blocked = new ScanItem { Path = "blocked", Name = "blocked", Selectable = false };
        blocked.Selected = true;
        Assert.False(blocked.Selected);

        var allowed = new ScanItem { Path = "allowed", Name = "allowed", Selectable = true };
        allowed.Selected = true;
        Assert.True(allowed.Selected);
    }

    [Fact]
    public void Default_rules_identify_known_paths_and_do_not_guess_unknown_paths()
    {
        var matcher = new PathRuleMatcher(RuleLibrary.LoadDefault());
        var temp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "old.tmp");
        var wsl = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "wsl", "Ubuntu", "ext4.vhdx");

        Assert.Equal("windows.local-temp", matcher.Classify(temp)?.RuleId);
        Assert.Equal("wsl.ext4-vhdx", matcher.Classify(wsl)?.RuleId);
        Assert.Equal("windows.winsxs", matcher.Classify(@"C:\Windows\WinSxS\amd64_test")?.RuleId);
        Assert.Null(matcher.Classify(@"C:\totally-unknown-xdisk\data.bin"));
    }

    [Fact]
    public async Task Scanner_aggregates_first_level_and_sorts_large_files()
    {
        using var fixture = new TempDirectory();
        var a = Directory.CreateDirectory(Path.Combine(fixture.Root, "A"));
        var b = Directory.CreateDirectory(Path.Combine(fixture.Root, "B"));
        await File.WriteAllBytesAsync(Path.Combine(a.FullName, "a.bin"), new byte[10]);
        await File.WriteAllBytesAsync(Path.Combine(b.FullName, "b.bin"), new byte[20]);
        await File.WriteAllBytesAsync(Path.Combine(fixture.Root, "root.bin"), new byte[5]);

        var scanner = new FileSystemScanner(new EmptyClassifier());
        var report = await scanner.ScanAsync(new ScanOptions { RootPath = fixture.Root, LargeFileThresholdBytes = 1, MaxLargeFiles = 10, ProgressBatchSize = 1 });

        Assert.True(report.IsComplete);
        Assert.Equal(35, report.AccessibleLogicalBytes);
        Assert.Equal(3, report.FileCount);
        Assert.Equal(20, report.MainOccupancies.Single(x => x.Name == "B").SizeBytes);
        Assert.Equal(10, report.MainOccupancies.Single(x => x.Name == "A").SizeBytes);
        Assert.Equal(5, report.MainOccupancies.Single(x => x.Name == "(根目录文件)").SizeBytes);
        Assert.Equal(new long[] { 20L, 10L, 5L }, report.LargeFiles.Select(x => x.SizeBytes).ToArray());
    }

    [Fact]
    public async Task Scanner_returns_an_incomplete_report_when_cancelled()
    {
        using var fixture = new TempDirectory();
        for (var i = 0; i < 25; i++) await File.WriteAllBytesAsync(Path.Combine(fixture.Root, $"{i}.bin"), new byte[8]);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var scanner = new FileSystemScanner(new EmptyClassifier());
        var report = await scanner.ScanAsync(new ScanOptions { RootPath = fixture.Root }, cancellationToken: cts.Token);

        Assert.False(report.IsComplete);
    }

    [Fact]
    public async Task Scanner_does_not_follow_directory_reparse_points_when_the_platform_allows_creating_one()
    {
        using var fixture = new TempDirectory();
        var outside = Directory.CreateDirectory(Path.Combine(fixture.Root, "outside"));
        await File.WriteAllBytesAsync(Path.Combine(outside.FullName, "secret.bin"), new byte[99]);
        var linkedParent = Directory.CreateDirectory(Path.Combine(fixture.Root, "scan"));
        var link = Path.Combine(linkedParent.FullName, "link");

        try { Directory.CreateSymbolicLink(link, outside.FullName); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException) { return; }

        var scanner = new FileSystemScanner(new EmptyClassifier());
        var report = await scanner.ScanAsync(new ScanOptions { RootPath = linkedParent.FullName, LargeFileThresholdBytes = 1 });

        Assert.Equal(0, report.FileCount);
        Assert.Contains(report.AccessIssues, x => x.Path == link && x.Reason.Contains("重解析点"));
    }

    [Fact]
    public void Safe_path_policy_rejects_roots_wildcards_virtual_disks_and_runtime_environments()
    {
        var policy = new SafePathPolicy();
        var driveRoot = Path.GetPathRoot(Environment.SystemDirectory)!;
        Assert.False(policy.Validate(driveRoot).IsSafe);
        Assert.False(policy.Validate(Path.Combine(Path.GetTempPath(), "*.tmp")).IsSafe);
        Assert.False(policy.Validate(Path.Combine(Path.GetTempPath(), "linux.vhdx")).IsSafe);
        Assert.False(policy.Validate(Path.Combine(Path.GetTempPath(), "project", ".venv", "python.exe")).IsSafe);
        Assert.False(policy.Validate(Path.Combine(Path.GetTempPath(), "project", "node_modules", "x.js")).IsSafe);
    }

    [Fact]
    public void Preview_rejects_incomplete_scans_and_ui_state_tampering()
    {
        using var fixture = new TempDirectory();
        var file = Path.Combine(fixture.Root, "cache.tmp");
        File.WriteAllText(file, "x");
        var matcher = CreateFixtureMatcher(fixture.Root, minAgeDays: null);
        var service = new CleanupPreviewService(matcher, new SafePathPolicy());

        var incomplete = new ScanReport { RootPath = fixture.Root, RuleVersion = matcher.RuleVersion, IsComplete = false };
        var selected = NewFixtureItem(file, matcher, selectable: true);
        selected.Selected = true;
        Assert.False(service.Build(incomplete, [selected]).IsExecutable);

        var tampered = NewFixtureItem(file, matcher, selectable: false);
        typeof(ScanItem).GetField("_selected", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(tampered, true);
        var complete = new ScanReport { RootPath = fixture.Root, RuleVersion = matcher.RuleVersion, IsComplete = true };
        var preview = service.Build(complete, [tampered]);
        Assert.False(preview.IsExecutable);
        Assert.Contains(preview.Errors, x => x.Contains("不可选择"));
    }

    [Fact]
    public void Preview_deduplicates_parent_child_targets_and_does_not_modify_files()
    {
        using var fixture = new TempDirectory();
        var parent = Directory.CreateDirectory(Path.Combine(fixture.Root, "cache"));
        var child = Path.Combine(parent.FullName, "item.tmp");
        File.WriteAllText(child, "unchanged");
        var matcher = CreateFixtureMatcher(parent.FullName, minAgeDays: null);
        var service = new CleanupPreviewService(matcher, new SafePathPolicy());
        var parentItem = NewFixtureItem(parent.FullName, matcher, selectable: true, kind: ScanItemKind.File);
        var childItem = NewFixtureItem(child, matcher, selectable: true);
        parentItem.Selected = true;
        childItem.Selected = true;
        var report = new ScanReport { RootPath = fixture.Root, RuleVersion = matcher.RuleVersion, IsComplete = true };

        var preview = service.Build(report, [parentItem, childItem]);

        Assert.True(preview.IsExecutable);
        Assert.Single(preview.Candidates);
        Assert.Equal(parent.FullName, preview.Candidates[0].Path, ignoreCase: true);
        Assert.Equal("unchanged", File.ReadAllText(child));
    }

    [Fact]
    public async Task Executor_only_deletes_files_that_still_satisfy_the_age_rule()
    {
        using var fixture = new TempDirectory();
        var root = Directory.CreateDirectory(Path.Combine(fixture.Root, "Temp"));
        var oldFile = Path.Combine(root.FullName, "old.tmp");
        var newFile = Path.Combine(root.FullName, "new.tmp");
        File.WriteAllText(oldFile, "old");
        File.WriteAllText(newFile, "new");
        File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow.AddDays(-10));
        File.SetLastWriteTimeUtc(newFile, DateTime.UtcNow.AddDays(-1));

        var matcher = CreateFixtureMatcher(root.FullName, minAgeDays: 7);
        var executor = new CleanupExecutor(matcher, new SafePathPolicy(), new FakeRecycleBinService());
        var preview = new CleanupPreview
        {
            RuleVersion = matcher.RuleVersion,
            Candidates =
            [
                NewCandidate(oldFile, matcher, 7),
                NewCandidate(newFile, matcher, 7)
            ]
        };

        var result = await executor.ExecuteAsync(preview, new CleanupExecutionOptions());

        Assert.False(File.Exists(oldFile));
        Assert.True(File.Exists(newFile));
        Assert.Equal(CleanupItemStatus.Deleted, result.Items[0].Status);
        Assert.Equal(CleanupItemStatus.Skipped, result.Items[1].Status);
    }

    [Fact]
    public async Task Executor_skips_a_file_that_changed_after_scan()
    {
        using var fixture = new TempDirectory();
        var root = Directory.CreateDirectory(Path.Combine(fixture.Root, "Cache"));
        var file = Path.Combine(root.FullName, "changed.tmp");
        File.WriteAllText(file, "v1");
        var scanned = File.GetLastWriteTimeUtc(file);
        var matcher = CreateFixtureMatcher(root.FullName, minAgeDays: null);
        File.SetLastWriteTimeUtc(file, scanned.AddMinutes(5));
        var candidate = NewCandidate(file, matcher, null) with { ScannedLastWriteTimeUtc = scanned };
        var executor = new CleanupExecutor(matcher, new SafePathPolicy(), new FakeRecycleBinService());

        var result = await executor.ExecuteAsync(new CleanupPreview { RuleVersion = matcher.RuleVersion, Candidates = [candidate] }, new CleanupExecutionOptions());

        Assert.True(File.Exists(file));
        Assert.Equal(CleanupItemStatus.Skipped, result.Items.Single().Status);
        Assert.Contains("发生变化", result.Items.Single().Message);
    }

    [Fact]
    public async Task Executor_stops_before_starting_the_next_item_after_cancellation()
    {
        using var fixture = new TempDirectory();
        var root = Directory.CreateDirectory(Path.Combine(fixture.Root, "Cache"));
        var a = Path.Combine(root.FullName, "a.tmp");
        var b = Path.Combine(root.FullName, "b.tmp");
        File.WriteAllText(a, "a");
        File.WriteAllText(b, "b");
        var matcher = CreateFixtureMatcher(root.FullName, minAgeDays: null);
        using var cts = new CancellationTokenSource();
        var recycle = new CancellingRecycleBinService(cts);

        var recycleRule = new DirectoryRule
        {
            Id = "fixture.recycle", PathPattern = root.FullName, Software = "fixture", Purpose = "fixture", Consequence = "fixture",
            RiskLevel = RiskLevel.Medium, Recommendation = CleanupRecommendation.Confirm, AllowCleanup = true,
            CleanupKind = CleanupKind.RecycleBin, AppliesToDescendants = true, Irreversible = true
        };
        matcher = new PathRuleMatcher(new RuleLibrary("test-stop", [recycleRule]));
        var executor = new CleanupExecutor(matcher, new SafePathPolicy(), recycle);
        var preview = new CleanupPreview
        {
            RuleVersion = matcher.RuleVersion,
            Candidates =
            [
                new CleanupCandidate(root.FullName, "fixture.recycle", matcher.RuleVersion, 0, null, CleanupKind.RecycleBin, null, RiskLevel.Medium, "fixture", true),
                new CleanupCandidate(b, "fixture.recycle", matcher.RuleVersion, 1, File.GetLastWriteTimeUtc(b), CleanupKind.RecycleBin, null, RiskLevel.Medium, "fixture", true)
            ]
        };

        var result = await executor.ExecuteAsync(preview, new CleanupExecutionOptions(true), cts.Token);

        Assert.True(result.Stopped);
        Assert.Single(result.Items);
        Assert.Equal(1, recycle.CallCount);
        Assert.Equal(Path.GetPathRoot(root.FullName), recycle.LastRootPath, ignoreCase: true);
    }

    [Fact]
    public async Task Reports_include_author_capacity_risk_consequence_and_recommendation()
    {
        using var fixture = new TempDirectory();
        var report = new ScanReport
        {
            RootPath = fixture.Root,
            RuleVersion = "test",
            DiskTotalBytes = 1000,
            DiskUsedBytes = 700,
            DiskFreeBytes = 300,
            AccessibleLogicalBytes = 500,
            HighlightedItems =
            [
                new ScanItem
                {
                    Path = Path.Combine(fixture.Root, "cache"), Name = "cache", Software = "Fixture", Purpose = "cache",
                    Consequence = "will be re-created", RiskLevel = RiskLevel.Medium,
                    Recommendation = CleanupRecommendation.Confirm, Selectable = false
                }
            ]
        };
        var jsonPath = Path.Combine(fixture.Root, "report.json");
        var htmlPath = Path.Combine(fixture.Root, "report.html");
        await new JsonReportWriter().WriteAsync(report, jsonPath);
        await new HtmlReportWriter().WriteAsync(report, htmlPath);
        var json = await File.ReadAllTextAsync(jsonPath);
        var html = await File.ReadAllTextAsync(htmlPath);

        foreach (var text in new[] { json, html })
        {
            Assert.Contains("Aix", text);
            Assert.Contains("xch03209527", text);
            Assert.Contains("Aix9527/Codex-Doctor", text);
            Assert.Contains("Fixture", text);
            Assert.Contains("will be re-created", text);
        }
        Assert.Contains("Medium", json);
        Assert.Contains("Confirm", json);
        Assert.Contains("容量概览", html);
    }

    [Fact]
    public void Optimization_advice_never_exposes_direct_delete_for_system_components_or_vhdx()
    {
        var advice = OptimizationAdvisor.CreateDefaultAdvice();
        var component = advice.Single(x => x.Title.Contains("组件存储"));
        var vhdx = advice.Single(x => x.Title.Contains("虚拟磁盘"));
        Assert.Equal("Dism", component.ActionKind);
        Assert.False((component.Arguments ?? string.Empty).Contains("del ", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Guidance", vhdx.ActionKind);
    }

    private static PathRuleMatcher CreateFixtureMatcher(string root, int? minAgeDays)
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
        return new PathRuleMatcher(new RuleLibrary("test-rules", [rule]));
    }

    private static ScanItem NewFixtureItem(string path, PathRuleMatcher matcher, bool selectable, ScanItemKind kind = ScanItemKind.File)
    {
        var c = matcher.Classify(path)!;
        return new ScanItem
        {
            Path = path,
            Name = Path.GetFileName(path),
            Kind = kind,
            SizeBytes = File.Exists(path) ? new FileInfo(path).Length : 0,
            Software = c.Software,
            Purpose = c.Purpose,
            Consequence = c.Consequence,
            RiskLevel = c.RiskLevel,
            Recommendation = c.Recommendation,
            CleanupRuleId = c.RuleId,
            CleanupKind = c.CleanupKind,
            Selectable = selectable,
            LastWriteTimeUtc = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null,
            MinAgeDays = c.MinAgeDays
        };
    }

    private static CleanupCandidate NewCandidate(string path, PathRuleMatcher matcher, int? minAgeDays)
        => new(path, "fixture.cache", matcher.RuleVersion, new FileInfo(path).Length, File.GetLastWriteTimeUtc(path), CleanupKind.File, minAgeDays, RiskLevel.Low, "Fixture data is deleted", false);

    private sealed class EmptyClassifier : IPathClassifier
    {
        public string RuleVersion => "test-empty";
        public PathClassification? Classify(string path) => null;
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Root { get; } = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "XDiskInspector.Tests", Guid.NewGuid().ToString("N"))).FullName;
        public void Dispose()
        {
            try { Directory.Delete(Root, true); } catch { }
        }
    }

    private sealed class FakeRecycleBinService : IRecycleBinService
    {
        public Task<(bool Success, string? Error)> EmptyAsync(string rootPath)
            => Task.FromResult<(bool, string?)>((true, null));
    }

    private sealed class CancellingRecycleBinService(CancellationTokenSource cts) : IRecycleBinService
    {
        public int CallCount { get; private set; }
        public string? LastRootPath { get; private set; }

        public Task<(bool Success, string? Error)> EmptyAsync(string rootPath)
        {
            CallCount++;
            LastRootPath = rootPath;
            cts.Cancel();
            return Task.FromResult<(bool, string?)>((true, null));
        }
    }
}
