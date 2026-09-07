using XDiskInspector.Cleanup;

namespace XDiskInspector.Tests;

public sealed class ProgramInstallTreeSafetyTests
{
    [Fact]
    public void Program_files_descendants_are_never_automatic_cleanup_targets()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        }
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

        Assert.NotEmpty(roots);
        var policy = new SafePathPolicy();

        foreach (var root in roots)
        {
            var result = policy.Validate(Path.Combine(root, "Vendor", "Product", "cache.tmp"));

            Assert.False(result.IsSafe);
            Assert.Contains("程序安装", result.Reason ?? string.Empty);
            Assert.Contains("子路径", result.Reason ?? string.Empty);
        }
    }
}
