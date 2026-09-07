using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using XDiskInspector.Core;

namespace XDiskInspector.Rules;

public sealed record DirectoryRule
{
    public required string Id { get; init; }
    public required string PathPattern { get; init; }
    public required string Software { get; init; }
    public required string Purpose { get; init; }
    public required string Consequence { get; init; }
    public RiskLevel RiskLevel { get; init; }
    public CleanupRecommendation Recommendation { get; init; }
    public bool AllowCleanup { get; init; }
    public CleanupKind CleanupKind { get; init; }
    public int? MinAgeDays { get; init; }
    public bool AppliesToDescendants { get; init; }
    public bool Irreversible { get; init; }
    public string? Guidance { get; init; }
}

public sealed class RuleLibrary
{
    public string Version { get; }
    public IReadOnlyList<DirectoryRule> Rules { get; }
    public RuleLibrary(string version, IEnumerable<DirectoryRule> rules) { Version = version; Rules = rules.ToArray(); }
    public static RuleLibrary LoadDefault()
    {
        var assembly = typeof(RuleLibrary).Assembly;
        var resourceName = assembly.GetManifestResourceNames().Single(x => x.EndsWith("directory-rules.json", StringComparison.OrdinalIgnoreCase));
        using var stream = assembly.GetManifestResourceStream(resourceName) ?? throw new InvalidOperationException("无法加载内置目录规则库。");
        using var document = JsonDocument.Parse(stream);
        var version = document.RootElement.GetProperty("version").GetString() ?? "unknown";
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new JsonStringEnumConverter());
        var rules = document.RootElement.GetProperty("rules").Deserialize<List<DirectoryRule>>(options) ?? [];
        return new RuleLibrary(version, rules);
    }
}

public sealed class PathRuleMatcher : IPathClassifier
{
    private sealed record CompiledRule(DirectoryRule Rule, Regex Regex, string ExpandedPattern, string MatchedRoot, int DeclarationOrder)
    {
        public int LiteralSpecificity => ExpandedPattern.Count(c => c != '*');
    }

    private readonly List<CompiledRule> _compiled;
    public string RuleVersion { get; }

    public PathRuleMatcher(RuleLibrary library)
    {
        RuleVersion = library.Version;
        _compiled = library.Rules
            .Select((rule, index) => Compile(rule, index))
            .OrderByDescending(x => x.LiteralSpecificity)
            .ThenBy(x => x.DeclarationOrder)
            .ToList();
    }

    public PathClassification? Classify(string path)
    {
        string normalized;
        try { normalized = Normalize(path); } catch { return null; }
        foreach (var entry in _compiled)
        {
            if (!entry.Regex.IsMatch(normalized)) continue;
            var root = entry.Rule.AppliesToDescendants ? entry.MatchedRoot : normalized;
            return new PathClassification(entry.Rule.Id, entry.Rule.Software, entry.Rule.Purpose, entry.Rule.Consequence, entry.Rule.RiskLevel, entry.Rule.Recommendation, entry.Rule.AllowCleanup, entry.Rule.CleanupKind, entry.Rule.MinAgeDays, entry.Rule.Guidance, root, entry.Rule.Irreversible);
        }
        return null;
    }

    public DirectoryRule? GetRule(string ruleId) => _compiled.FirstOrDefault(x => string.Equals(x.Rule.Id, ruleId, StringComparison.Ordinal))?.Rule;

    private static CompiledRule Compile(DirectoryRule rule, int declarationOrder)
    {
        var expanded = Normalize(Environment.ExpandEnvironmentVariables(rule.PathPattern));
        var regexText = string.Join(@"[^\\]*", expanded.Split('*').Select(Regex.Escape));
        regexText = rule.AppliesToDescendants ? $"^{regexText}(?:\\\\.*)?$" : $"^{regexText}$";
        var matchedRoot = rule.AppliesToDescendants ? expanded[..(expanded.IndexOf('*') >= 0 ? expanded.IndexOf('*') : expanded.Length)].TrimEnd('\\') : expanded;
        return new CompiledRule(
            rule,
            new Regex(regexText, RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            expanded,
            matchedRoot,
            declarationOrder);
    }

    private static string Normalize(string path)
    {
        var full = Path.GetFullPath(path).Replace('/', '\\');
        if (full.Length > 3) full = full.TrimEnd('\\');
        return full;
    }
}
