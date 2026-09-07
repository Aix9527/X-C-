using XDiskInspector.Core;
using XDiskInspector.Rules;

namespace XDiskInspector.Tests;

public sealed class RuleSpecificityTests
{
    [Fact]
    public void More_specific_child_rule_wins_even_when_broad_parent_is_declared_first()
    {
        var root = Path.Combine(Path.GetTempPath(), "XDiskInspector.Tests", "specificity");
        var broad = Rule("broad", root, allowCleanup: false, recommendation: CleanupRecommendation.Keep);
        var specificRoot = Path.Combine(root, "cache");
        var specific = Rule("specific", specificRoot, allowCleanup: true, recommendation: CleanupRecommendation.Confirm);
        var matcher = new PathRuleMatcher(new RuleLibrary("specificity-test", [broad, specific]));

        var classification = matcher.Classify(Path.Combine(specificRoot, "item.bin"));

        Assert.NotNull(classification);
        Assert.Equal("specific", classification!.RuleId);
        Assert.True(classification.AllowCleanup);
    }

    [Fact]
    public void Declaration_order_is_preserved_for_rules_with_equal_specificity()
    {
        var root = Path.Combine(Path.GetTempPath(), "XDiskInspector.Tests", "equal-specificity");
        var first = Rule("first", root, allowCleanup: false, recommendation: CleanupRecommendation.Keep);
        var second = Rule("second", root, allowCleanup: true, recommendation: CleanupRecommendation.Confirm);
        var matcher = new PathRuleMatcher(new RuleLibrary("equal-specificity-test", [first, second]));

        var classification = matcher.Classify(Path.Combine(root, "item.bin"));

        Assert.NotNull(classification);
        Assert.Equal("first", classification!.RuleId);
    }

    private static DirectoryRule Rule(string id, string root, bool allowCleanup, CleanupRecommendation recommendation)
        => new()
        {
            Id = id,
            PathPattern = root,
            Software = id,
            Purpose = id,
            Consequence = id,
            RiskLevel = allowCleanup ? RiskLevel.Medium : RiskLevel.High,
            Recommendation = recommendation,
            AllowCleanup = allowCleanup,
            CleanupKind = allowCleanup ? CleanupKind.File : CleanupKind.None,
            AppliesToDescendants = true
        };
}
