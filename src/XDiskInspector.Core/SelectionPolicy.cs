namespace XDiskInspector.Core;

public static class SelectionPolicy
{
    public static int SelectSuggested(IEnumerable<ScanItem> visibleItems)
    {
        var selected = 0;
        foreach (var item in visibleItems)
        {
            if (!CanSelect(item)) continue;
            if (item.Recommendation != CleanupRecommendation.Suggested || item.RiskLevel > RiskLevel.Medium) continue;
            item.Selected = true;
            if (item.Selected) selected++;
        }
        return selected;
    }

    public static int SelectAll(IEnumerable<ScanItem> visibleItems)
    {
        var selected = 0;
        foreach (var item in visibleItems)
        {
            if (!CanSelect(item)) continue;
            item.Selected = true;
            if (item.Selected) selected++;
        }
        return selected;
    }

    public static int SelectSoftware(IEnumerable<ScanItem> visibleItems, string? software)
    {
        if (string.IsNullOrWhiteSpace(software)) return 0;

        var selected = 0;
        foreach (var item in visibleItems)
        {
            if (!CanSelect(item)) continue;
            if (!string.Equals(item.Software, software, StringComparison.OrdinalIgnoreCase)) continue;
            item.Selected = true;
            if (item.Selected) selected++;
        }
        return selected;
    }

    private static bool CanSelect(ScanItem item)
        => item.Selectable && !string.IsNullOrWhiteSpace(item.CleanupRuleId);
}
