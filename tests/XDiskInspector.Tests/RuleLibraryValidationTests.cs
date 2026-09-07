using XDiskInspector.Core;
using XDiskInspector.Rules;

namespace XDiskInspector.Tests;

public sealed class RuleLibraryValidationTests
{
    [Fact]
    public void Duplicate_rule_ids_are_rejected()
    {
        var first = Rule("duplicate", allowCleanup: false, CleanupKind.None);
        var second = Rule("duplicate", allowCleanup: true, CleanupKind.File);

        var ex = Assert.Throws<InvalidDataException>(() => new RuleLibrary("test", [first, second]));

        Assert.Contains("重复", ex.Message);
    }

    [Fact]
    public void Cleanup_permission_and_cleanup_kind_must_be_consistent()
    {
        Assert.Throws<InvalidDataException>(() =>
            new RuleLibrary("test", [Rule("allowed-none", allowCleanup: true, CleanupKind.None)]));

        Assert.Throws<InvalidDataException>(() =>
            new RuleLibrary("test", [Rule("blocked-file", allowCleanup: false, CleanupKind.File)]));
    }

    [Fact]
    public void Negative_minimum_age_is_rejected()
    {
        var rule = Rule("negative-age", allowCleanup: true, CleanupKind.File) with { MinAgeDays = -1 };

        Assert.Throws<InvalidDataException>(() => new RuleLibrary("test", [rule]));
    }

    [Fact]
    public void Default_embedded_library_satisfies_validation_contract()
    {
        var library = RuleLibrary.LoadDefault();

        Assert.NotEmpty(library.Rules);
        Assert.False(string.IsNullOrWhiteSpace(library.Version));
    }

    private static DirectoryRule Rule(string id, bool allowCleanup, CleanupKind cleanupKind)
        => new()
        {
            Id = id,
            PathPattern = Path.Combine(Path.GetTempPath(), "XDiskInspector.Tests", id),
            Software = id,
            Purpose = id,
            Consequence = id,
            RiskLevel = RiskLevel.Low,
            Recommendation = allowCleanup ? CleanupRecommendation.Confirm : CleanupRecommendation.Keep,
            AllowCleanup = allowCleanup,
            CleanupKind = cleanupKind,
            AppliesToDescendants = true
        };
}
