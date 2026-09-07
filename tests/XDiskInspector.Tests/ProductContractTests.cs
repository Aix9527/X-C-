using XDiskInspector.Cleanup;
using XDiskInspector.Core;
using XDiskInspector.Rules;

namespace XDiskInspector.Tests;

public sealed class ProductContractTests
{
    [Fact]
    public void Select_suggested_only_changes_the_current_visible_safe_scope()
    {
        var visibleSafe = Item("visible-safe", true, "rule.safe", CleanupRecommendation.Suggested, RiskLevel.Low);
        var hiddenSafe = Item("hidden-safe", true, "rule.safe", CleanupRecommendation.Suggested, RiskLevel.Low);
        var blocked = Item("blocked", false, null, CleanupRecommendation.Suggested, RiskLevel.Low);
        var confirm = Item("confirm", true, "rule.confirm", CleanupRecommendation.Confirm, RiskLevel.Medium);

        var count = SelectionPolicy.SelectSuggested([visibleSafe, blocked, confirm]);

        Assert.Equal(1, count);
        Assert.True(visibleSafe.Selected);
        Assert.False(hiddenSafe.Selected);
        Assert.False(blocked.Selected);
        Assert.False(confirm.Selected);
    }

    [Fact]
    public void Safe_path_policy_rejects_user_root_and_windows_critical_directories()
    {
        var policy = new SafePathPolicy();
        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        if (!string.IsNullOrWhiteSpace(user)) Assert.False(policy.Validate(user).IsSafe);
        if (!string.IsNullOrWhiteSpace(windows))
        {
            Assert.False(policy.Validate(Path.Combine(windows, "WinSxS", "amd64_fixture")).IsSafe);
            Assert.False(policy.Validate(Path.Combine(windows, "Installer", "fixture.msi")).IsSafe);
            Assert.False(policy.Validate(Path.Combine(windows, "System32", "DriverStore", "fixture.sys")).IsSafe);
        }
    }

    [Fact]
    public void Default_rule_library_contains_the_full_first_release_knowledge_set()
    {
        var ids = RuleLibrary.LoadDefault().Rules.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        string[] required =
        [
            "windows.pagefile", "windows.recycle-bin", "windows.local-temp", "windows.crash-dumps",
            "wsl.ext4-vhdx", "docker.data", "omnivoice.hf-cache", "omnivoice.venv",
            "jianying.data", "trae.data", "tencent.data", "bun.install-cache", "bun.root",
            "npm.cache", "pnpm.store", "pnpm.cache", "user.generic-cache",
            "windows.winsxs", "windows.installer", "nvidia.ota-artifacts"
        ];

        Assert.All(required, id => Assert.Contains(id, ids));
    }

    [Fact]
    public void Cleanup_candidates_explain_recovery_semantics_in_plain_language()
    {
        var file = new CleanupCandidate(
            @"C:\Temp\old.tmp", "fixture.file", "rules", 10, DateTime.UtcNow,
            CleanupKind.File, null, RiskLevel.Low, "cache is recreated", false);
        var recycle = new CleanupCandidate(
            @"C:\$Recycle.Bin", "fixture.recycle", "rules", 20, null,
            CleanupKind.RecycleBin, null, RiskLevel.Medium, "deleted files are permanently removed", true);

        Assert.Contains("文件本身不可恢复", file.RecoveryText);
        Assert.Contains("无法再从回收站恢复", recycle.RecoveryText);
    }

    private static ScanItem Item(string path, bool selectable, string? ruleId, CleanupRecommendation recommendation, RiskLevel risk)
        => new()
        {
            Path = path,
            Name = path,
            Selectable = selectable,
            CleanupRuleId = ruleId,
            Recommendation = recommendation,
            RiskLevel = risk
        };
}
