using System.Runtime.CompilerServices;

namespace XDiskInspector.Core;

public static class ScanReportRuntimeState
{
    private sealed class Marker;
    private static readonly ConditionalWeakTable<ScanReport, Marker> PersistedReports = new();

    public static void MarkPersisted(ScanReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        PersistedReports.Remove(report);
        PersistedReports.Add(report, new Marker());
    }

    public static bool IsPersisted(ScanReport? report)
        => report is not null && PersistedReports.TryGetValue(report, out _);
}
