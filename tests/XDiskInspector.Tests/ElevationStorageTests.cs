using System.Security.AccessControl;
using System.Security.Principal;
using XDiskInspector.Cleanup;

namespace XDiskInspector.Tests;

/// <summary>
/// Regression tests for the elevation request handoff channel (hotfix2):
/// requests must live under %ProgramData% with an ACL that lets the standard-user parent
/// write and the elevated administrator worker read.
/// </summary>
public sealed class ElevationStorageTests
{
    [Fact]
    public void Root_directory_points_under_program_data()
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var root = ElevationStorage.RootDirectory;

        Assert.StartsWith(programData + Path.DirectorySeparatorChar, root, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("XDiskInspector", "Elevation"), root, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ensure_accessible_creates_directory_and_allows_current_user_to_write_a_request_file()
    {
        if (!OperatingSystem.IsWindows()) return;

        // Guard: the test must only assert behavior for a directory we are allowed to create.
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (string.IsNullOrWhiteSpace(programData) || !Directory.Exists(programData)) return;

        ElevationStorage.EnsureAccessible();
        Assert.True(Directory.Exists(ElevationStorage.RootDirectory), "Elevation directory should exist after EnsureAccessible.");

        var probe = Path.Combine(ElevationStorage.RootDirectory, $"write-probe-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(probe, "{}");
            Assert.True(File.Exists(probe), "Standard user should be able to create a request file.");
            Assert.Equal("{}", File.ReadAllText(probe));
        }
        finally
        {
            try { File.Delete(probe); } catch { }
        }
    }

    [Fact]
    public void Elevation_directory_acl_grants_users_modify_and_administrators_full_control()
    {
        if (!OperatingSystem.IsWindows()) return;

        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (string.IsNullOrWhiteSpace(programData) || !Directory.Exists(programData)) return;

        ElevationStorage.EnsureAccessible();

        var security = new DirectoryInfo(ElevationStorage.RootDirectory).GetAccessControl();
        var rules = security.GetAccessRules(true, true, typeof(NTAccount))
            .OfType<FileSystemAccessRule>()
            .Where(r => r.AccessControlType == AccessControlType.Allow)
            .ToArray();

        Assert.Contains(rules, r =>
            r.IdentityReference.Value.Equals("BUILTIN\\Users", StringComparison.OrdinalIgnoreCase) &&
            (r.FileSystemRights & FileSystemRights.Modify) == FileSystemRights.Modify);

        Assert.Contains(rules, r =>
            r.IdentityReference.Value.Equals("BUILTIN\\Administrators", StringComparison.OrdinalIgnoreCase) &&
            (r.FileSystemRights & FileSystemRights.FullControl) == FileSystemRights.FullControl);
    }

    [Theory]
    [InlineData(ElevationErrorCodes.Success, "成功")]
    [InlineData(ElevationErrorCodes.RequestNotFound, "不存在")]
    [InlineData(ElevationErrorCodes.RequestAccessDenied, "拒绝")]
    [InlineData(ElevationErrorCodes.RequestLocked, "占用")]
    [InlineData(ElevationErrorCodes.RequestInvalid, "无效")]
    [InlineData(ElevationErrorCodes.NotElevated, "管理员")]
    public void Error_code_descriptions_are_specific(int code, string expectedFragment)
    {
        var description = ElevationErrorCodes.Describe(code);
        Assert.Contains(expectedFragment, description, StringComparison.Ordinal);
    }
}
