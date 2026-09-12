namespace ShellCommand.Core;

public static class Conditions
{
    public static Truth Evaluate(Condition condition, MenuContext context, IReadOnlyList<string>? facts)
    {
        var children = condition.Children ?? [];
        return condition.Operator switch
        {
            "exists" => facts is null || context.Directory is null ? Truth.Unknown
                : facts.Any(entry => WildcardMatch(condition.Value!, entry)) ? Truth.True : Truth.False,
            "context" => (condition.Value == "selection") == context.IsSelection ? Truth.True : Truth.False,
            "selection" => MatchSelection(condition.Selection!, context),
            "not" => Evaluate(children[0], context, facts) switch { Truth.True => Truth.False, Truth.False => Truth.True, _ => Truth.Unknown },
            "all" => Combine(children.Select(c => Evaluate(c, context, facts)), true),
            "any" => Combine(children.Select(c => Evaluate(c, context, facts)), false),
            _ => Truth.Unknown
        };
    }
    public static bool Contains(Condition? condition, string op) => condition is not null &&
        (condition.Operator == op || condition.Children?.Any(c => Contains(c, op)) == true);
    private static Truth Combine(IEnumerable<Truth> values, bool all)
    {
        var unknown = false;
        foreach (var value in values)
        {
            if (all && value == Truth.False) return Truth.False;
            if (!all && value == Truth.True) return Truth.True;
            unknown |= value == Truth.Unknown;
        }
        return unknown ? Truth.Unknown : all ? Truth.True : Truth.False;
    }
    private static Truth MatchSelection(SelectionRule rule, MenuContext context)
    {
        if (context.Selection.Count < rule.Min || context.Selection.Count > rule.Max || !context.IsSelection) return Truth.False;
        return context.Selection.All(item =>
            (rule.Types is null || rule.Types.Contains(item.IsFolder ? "folder" : "file", StringComparer.Ordinal)) &&
            (rule.Extensions is null || !item.IsFolder && rule.Extensions.Contains(Path.GetExtension(item.Path), StringComparer.OrdinalIgnoreCase))) ? Truth.True : Truth.False;
    }
    public static bool WildcardMatch(string pattern, string value)
    {
        var p = 0; var v = 0; var star = -1; var checkpoint = 0;
        while (v < value.Length)
        {
            if (p < pattern.Length && (pattern[p] == '?' || char.ToUpperInvariant(pattern[p]) == char.ToUpperInvariant(value[v]))) { p++; v++; }
            else if (p < pattern.Length && pattern[p] == '*') { star = p++; checkpoint = v; }
            else if (star >= 0) { p = star + 1; v = ++checkpoint; }
            else return false;
        }
        while (p < pattern.Length && pattern[p] == '*') p++;
        return p == pattern.Length;
    }
}
