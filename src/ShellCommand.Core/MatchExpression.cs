namespace ShellCommand.Core;

public sealed class MatchExpression
{
    public const int MaxTerms = 64;
    private readonly IReadOnlyList<Term> _terms;

    private MatchExpression(IReadOnlyList<Term> terms) => _terms = terms;

    public static bool TryParse(string? value, out MatchExpression? expression, out string? error)
    {
        expression = null;
        error = null;
        if (string.IsNullOrWhiteSpace(value))
            return true;

        var pieces = value.Split("<&&>", StringSplitOptions.None);
        if (pieces.Length > MaxTerms)
        {
            error = $"A Match expression may contain at most {MaxTerms} terms.";
            return false;
        }

        var terms = new List<Term>(pieces.Length);
        foreach (var raw in pieces)
        {
            var text = raw.Trim();
            var negated = text.StartsWith('!');
            if (negated) text = text[1..].Trim();

            if (text.Length == 0 || text is "." or ".." || text.Contains('/') || text.Contains('\\') || text.Contains("..", StringComparison.Ordinal))
            {
                error = $"Invalid Match term '{raw}'. Terms must be direct child names.";
                return false;
            }

            terms.Add(new Term(text, negated));
        }

        expression = new MatchExpression(terms);
        return true;
    }

    public bool IsMatch(IDirectoryFacts facts)
        => _terms.All(term => term.Negated != facts.Exists(term.Pattern));

    private sealed record Term(string Pattern, bool Negated);

    public static bool WildcardMatch(string pattern, string value)
    {
        var p = 0;
        var v = 0;
        var star = -1;
        var checkpoint = 0;
        while (v < value.Length)
        {
            if (p < pattern.Length && (pattern[p] == '?' || char.ToUpperInvariant(pattern[p]) == char.ToUpperInvariant(value[v])))
            {
                p++;
                v++;
            }
            else if (p < pattern.Length && pattern[p] == '*')
            {
                star = p++;
                checkpoint = v;
            }
            else if (star >= 0)
            {
                p = star + 1;
                v = ++checkpoint;
            }
            else
            {
                return false;
            }
        }

        while (p < pattern.Length && pattern[p] == '*') p++;
        return p == pattern.Length;
    }
}
