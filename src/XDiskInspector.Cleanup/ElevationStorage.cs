using System.Security.AccessControl;
using System.Security.Principal;

namespace XDiskInspector.Cleanup;

/// <summary>
/// Stores cross-integrity-level elevation requests under %ProgramData% so that both the
/// standard-user parent process and the elevated administrator worker can access them.
/// %LOCALAPPDATA% is owned by the current user profile and its default ACL is not suited
/// for administrator-worker handoff.
/// </summary>
public static class ElevationStorage
{
    private const string ProductFolder = "XDiskInspector";
    private const string ElevationFolder = "Elevation";
    private const string LogsFolder = "Logs";
    private const string ElevationLogFile = "elevation.log";

    public static string ProductDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        ProductFolder);

    public static string RootDirectory => Path.Combine(ProductDirectory, ElevationFolder);

    public static string LogDirectory => Path.Combine(ProductDirectory, LogsFolder);

    /// <summary>
    /// Creates the elevation request directory (if missing) and guarantees its ACL allows
    /// SYSTEM and Administrators full control, plus BUILTIN\Users modify access so the
    /// standard-user parent process can write request files the elevated worker can read.
    /// </summary>
    public static void EnsureAccessible()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Elevation storage requires Windows.");

        var directory = Directory.CreateDirectory(RootDirectory);
        var security = directory.GetAccessControl();

        var inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        security.AddAccessRule(new FileSystemAccessRule(
            "SYSTEM", FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            "BUILTIN\\Administrators", FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            "BUILTIN\\Users", FileSystemRights.Modify, inheritance, PropagationFlags.None, AccessControlType.Allow));

        directory.SetAccessControl(security);
    }

    /// <summary>Appends a timestamped diagnostic line to the elevation log (best effort).</summary>
    public static void AppendLog(string message)
    {
        try
        {
            if (!OperatingSystem.IsWindows()) return;
            Directory.CreateDirectory(LogDirectory);
            File.AppendAllText(
                Path.Combine(LogDirectory, ElevationLogFile),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging is best effort and must never break cleanup.
        }
    }
}

/// <summary>Well-known exit codes used by the elevated cleanup worker and their descriptions.</summary>
public static class ElevationErrorCodes
{
    public const int Success = 0;
    public const int BadInvocation = 2;
    public const int UnexpectedFailure = 4;
    public const int NotElevated = 5;

    /// <summary>The .request.json file does not exist at the expected path.</summary>
    public const int RequestNotFound = 10;

    /// <summary>The .request.json file exists but access was denied.</summary>
    public const int RequestAccessDenied = 11;

    /// <summary>The .request.json file is locked/being written by another process.</summary>
    public const int RequestLocked = 12;

    /// <summary>The .request.json content is invalid or cannot be deserialized.</summary>
    public const int RequestInvalid = 13;

    public static string Describe(int code) => code switch
    {
        Success => "管理员清理成功。",
        BadInvocation => "管理员清理进程收到无效调用参数。",
        UnexpectedFailure => "管理员清理进程发生未预期的错误。",
        NotElevated => "管理员清理进程未以管理员权限运行。",
        RequestNotFound => "管理员清理请求文件不存在。",
        RequestAccessDenied => "管理员清理请求文件读取被拒绝（权限不足）。",
        RequestLocked => "管理员清理请求文件被占用或尚未写入完成。",
        RequestInvalid => "管理员清理请求内容无效或格式损坏。",
        _ => $"管理员清理进程返回未知退出码 {code}。"
    };
}
