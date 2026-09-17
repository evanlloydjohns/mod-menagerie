using System.Text.RegularExpressions;

namespace ModMenagerie.Domain;

/// <summary>Only numeric release identifiers, trailing .x families, or inclusive equal-length ranges.</summary>
public static class VersionPatterns
{
    private static int[]? Parts(string text)
    {
        if (!Regex.IsMatch(text, @"^(0|[1-9][0-9]{0,5})(\.(0|[1-9][0-9]{0,5})){0,3}$")) return null;
        return text.Split('.').Select(int.Parse).ToArray();
    }
    private static int Compare(int[] a, int[] b)
    {
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i]) return a[i].CompareTo(b[i]);
        return 0;
    }
    public static bool IsValid(string pattern)
    {
        if (pattern.EndsWith(".x", StringComparison.Ordinal)) return Parts(pattern[..^2]) is { Length: < 4 };
        var range = pattern.Split(['–', '-']);
        return range.Length == 2 && Parts(range[0]) is { } a && Parts(range[1]) is { } b && a.Length == b.Length && Compare(a, b) <= 0;
    }
    public static bool Matches(string pattern, string target)
    {
        if (!IsValid(pattern) || Parts(target) is not { } t) return false;
        if (pattern.EndsWith(".x", StringComparison.Ordinal))
        {
            var prefix = Parts(pattern[..^2])!;
            return t.Length > prefix.Length && t.Take(prefix.Length).SequenceEqual(prefix);
        }
        var range = pattern.Split(['–', '-']);
        var a = Parts(range[0])!;
        var b = Parts(range[1])!;
        return a.Length == t.Length && Compare(a, t) <= 0 && Compare(t, b) <= 0;
    }
}
