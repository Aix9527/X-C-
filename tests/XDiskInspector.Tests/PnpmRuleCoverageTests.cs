using XDiskInspector.Core;
using XDiskInspector.Rules;

namespace XDiskInspector.Tests;

public sealed class PnpmRuleCoverageTests
{
    [Fact]
    public void Current_windows_default_pnpm_store_is_recognized_as_confirm_only_cleanup()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData)) return;

        var matcher = new PathRuleMatcher(RuleLibrary.LoadDefault());
        var storeFile = Path.Combine(localAppData, "pnpm", "store", "v10", "files", "ab", "cached-package");

        var classification = matcher.Classify(storeFile);

        Assert.NotNull(classification);
        Assert.Equal("pnpm.store", classification!.RuleId);
        Assert.True(classification.AllowCleanup);
        Assert.Equal(CleanupRecommendation.Confirm, classification.Recommendation);
        Assert.Equal(RiskLevel.Medium, classification.RiskLevel);
        Assert.Equal(CleanupKind.File, classification.CleanupKind);
    }

    [Fact]
    public void Legacy_pnpm_cache_path_remains_supported_for_design_compatibility()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData)) return;

        var matcher = new PathRuleMatcher(RuleLibrary.LoadDefault());
        var legacyFile = Path.Combine(localAppData, "pnpm-cache", "metadata.json");

        var classification = matcher.Classify(legacyFile);

        Assert.NotNull(classification);
        Assert.Equal("pnpm.cache", classification!.RuleId);
        Assert.True(classification.AllowCleanup);
        Assert.Equal(CleanupRecommendation.Confirm, classification.Recommendation);
    }

    [Fact]
    public void Pnpm_store_is_never_selected_by_select_suggested_policy()
    {
        var item = new ScanItem
        {
            Path = "pnpm-store-fixture",
            Name = "pnpm-store-fixture",
            Selectable = true,
            CleanupRuleId = "pnpm.store",
            Recommendation = CleanupRecommendation.Confirm,
            RiskLevel = RiskLevel.Medium
        };

        Assert.False(SelectionPolicy.IsSuggestedSafeCandidate(item));
    }
}
