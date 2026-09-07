using XDiskInspector.App;
using XDiskInspector.Core;

namespace XDiskInspector.Tests;

/// <summary>
/// Regression coverage for Issue #12 candidate-pool refresh logic:
/// after a successful delete the candidate list should drop the deleted item,
/// and a manual refresh should drop items whose files no longer exist on disk.
/// </summary>
public sealed class CandidateFreshnessTests
{
    [Fact]
    public void Existing_file_is_present()
    {
        using var fixture = new TempDirectory();
        var path = Path.Combine(fixture.Root, "present.tmp");
        File.WriteAllText(path, "x");
        Assert.True(CandidateFreshness.IsPresent(path, ScanItemKind.File));
    }

    [Fact]
    public void Missing_file_is_not_present()
    {
        var path = Path.Combine(Path.GetTempPath(), "XDI-missing-" + Guid.NewGuid().ToString("N") + ".tmp");
        Assert.False(CandidateFreshness.IsPresent(path, ScanItemKind.File));
    }

    [Fact]
    public void Special_item_is_present_when_directory_exists()
    {
        using var fixture = new TempDirectory();
        Assert.True(CandidateFreshness.IsPresent(fixture.Root, ScanItemKind.Special));
    }

    [Fact]
    public void Empty_path_is_not_present()
    {
        Assert.False(CandidateFreshness.IsPresent(string.Empty, ScanItemKind.File));
        Assert.False(CandidateFreshness.IsPresent("   ", ScanItemKind.Special));
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Root { get; } = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "XDiskInspector.Tests", Guid.NewGuid().ToString("N"))).FullName;

        public void Dispose()
        {
            try { Directory.Delete(Root, true); } catch { }
        }
    }
}
