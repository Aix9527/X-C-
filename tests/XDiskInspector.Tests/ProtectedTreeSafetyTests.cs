using XDiskInspector.Cleanup;

namespace XDiskInspector.Tests;

public sealed class ProtectedTreeSafetyTests
{
    [Fact]
    public void Personal_directory_descendants_are_never_automatic_cleanup_targets()
    {
        var policy = new SafePathPolicy();
        var roots = new List<string>();

        AddIfPresent(roots, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        AddIfPresent(roots, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        AddIfPresent(roots, Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(profile)) AddIfPresent(roots, Path.Combine(profile, "Downloads"));

        Assert.NotEmpty(roots);
        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var child = Path.Combine(root, "XDiskInspector-Protected", "personal-file.tmp");
            var result = policy.Validate(child);

            Assert.False(result.IsSafe);
            Assert.Contains("子路径", result.Reason ?? string.Empty);
        }
    }

    [Fact]
    public void Extra_forbidden_roots_protect_the_entire_subtree()
    {
        var protectedRoot = Path.Combine(Path.GetTempPath(), "XDiskInspector.Tests", Guid.NewGuid().ToString("N"), "Protected");
        var policy = new SafePathPolicy([protectedRoot]);

        var result = policy.Validate(Path.Combine(protectedRoot, "child", "file.tmp"));

        Assert.False(result.IsSafe);
        Assert.Contains("子路径", result.Reason ?? string.Empty);
    }

    [Fact]
    public void User_profile_root_protection_does_not_blanket_block_local_app_data()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData)) return;

        var policy = new SafePathPolicy();
        var candidate = Path.Combine(localAppData, "Temp", $"xdisk-{Guid.NewGuid():N}.tmp");
        var result = policy.Validate(candidate);

        Assert.True(result.IsSafe, result.Reason);
    }

    private static void AddIfPresent(List<string> roots, string? path)
    {
        if (!string.IsNullOrWhiteSpace(path)) roots.Add(Path.GetFullPath(path));
    }
}
