using System.IO;
using XDiskInspector.Core;

namespace XDiskInspector.App;

/// <summary>
/// Helps keep the cleanup candidate list in sync with the real file system.
/// Deleted items are removed from the candidate pool after a successful cleanup
/// (Issue #12) instead of lingering until the next rescan.
/// </summary>
public static class CandidateFreshness
{
    /// <summary>
    /// Returns whether a candidate item still exists on disk.
    /// Special items (e.g. the recycle bin) are treated as present when the
    /// underlying directory still exists; file/directory candidates use existence
    /// checks on the path itself.
    /// </summary>
    public static bool IsPresent(string path, ScanItemKind kind)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            return kind == ScanItemKind.Special
                ? Directory.Exists(path)
                : File.Exists(path);
        }
        catch
        {
            // Unreadable or invalid paths are treated as stale so they can be refreshed out.
            return false;
        }
    }
}
