using XDiskInspector.Rules;

namespace XDiskInspector.Tests;

public sealed class BunRuleSafetyTests
{
    [Fact]
    public void Bun_package_cache_is_allowed_but_bin_and_other_root_content_are_kept()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(profile)) return;

        var matcher = new PathRuleMatcher(RuleLibrary.LoadDefault());

        var cacheFile = Path.Combine(profile, ".bun", "install", "cache", "react@19.0.0", "package.json");
        var binFile = Path.Combine(profile, ".bun", "bin", "bun.exe");
        var globalFile = Path.Combine(profile, ".bun", "install", "global", "node_modules", "tool", "package.json");

        var cache = matcher.Classify(cacheFile);
        var bin = matcher.Classify(binFile);
        var global = matcher.Classify(globalFile);

        Assert.NotNull(cache);
        Assert.Equal("bun.install-cache", cache!.RuleId);
        Assert.True(cache.AllowCleanup);

        Assert.NotNull(bin);
        Assert.Equal("bun.root", bin!.RuleId);
        Assert.False(bin.AllowCleanup);

        Assert.NotNull(global);
        Assert.Equal("bun.root", global!.RuleId);
        Assert.False(global.AllowCleanup);
    }

    [Fact]
    public void Bun_specific_cache_rule_precedes_the_broader_bun_root_rule()
    {
        var library = RuleLibrary.LoadDefault();
        var ids = library.Rules.Select(x => x.Id).ToList();

        Assert.True(ids.IndexOf("bun.install-cache") >= 0);
        Assert.True(ids.IndexOf("bun.root") >= 0);
        Assert.True(ids.IndexOf("bun.install-cache") < ids.IndexOf("bun.root"));
    }
}
