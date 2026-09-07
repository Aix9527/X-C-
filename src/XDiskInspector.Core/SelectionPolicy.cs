namespace XDiskInspector.Core;

public static class SelectionPolicy
{
    public static int SelectSuggested(IEnumerable<ScanItem> visibleItems)
    {
        var selected = 0;
        foreach (var item in visibleItems)
        {
            if (!item.Selectable || string.IsNullOrWhiteSpace(item.CleanupRuleId)) continue;
            if (item.Recommendation != CleanupRecommendation.Suggested || item.RiskLevel > RiskLevel.Medium) continue;
            item.Selected = true;
            if (item.Selected) selected++;
        }
        return selected;
    }
}
