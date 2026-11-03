using System.Text.RegularExpressions;

namespace Idk.Services;

/// <summary>Cleans noisy text copied from web pages before showing it as selection context.</summary>
public static class SelectionSnippetFormatter
{
    private static readonly Regex JunkCharRx = new(@"[\uFFFC\u200B-\u200D\uFEFF\u00AD]", RegexOptions.Compiled);
    private static readonly Regex MultiSpaceRx = new(@"[^\S\n]{2,}", RegexOptions.Compiled);
    private static readonly Regex MultiNewlineRx = new(@"\n{3,}", RegexOptions.Compiled);

    public static string SanitizeForDisplay(string? raw, int maxChars = 1600)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";

        var t = JunkCharRx.Replace(raw, " ");
        t = t.Replace("\r\n", "\n");
        t = MultiSpaceRx.Replace(t, " ");
        t = MultiNewlineRx.Replace(t, "\n\n");
        t = t.Trim();

        if (t.Length <= maxChars) return t;
        var cut = t[..maxChars];
        var lastBreak = cut.LastIndexOf('\n');
        if (lastBreak > maxChars / 2)
            cut = cut[..lastBreak];
        return cut.TrimEnd() + "…";
    }

    /// <summary>One short line of the highlight, for a raised preview above the chat.</summary>
    public static string ToPreview(string? raw, int maxChars = 140)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var t = JunkCharRx.Replace(raw, " ");
        t = t.Replace('\r', ' ').Replace('\n', ' ');
        t = MultiSpaceRx.Replace(t, " ").Trim();
        if (t.Length <= maxChars) return t;
        var cut = t[..maxChars];
        var space = cut.LastIndexOf(' ');
        if (space > maxChars / 2)
            cut = cut[..space];
        return cut.TrimEnd() + "…";
    }

    public static IReadOnlyList<string> SplitParagraphs(string? text)
    {
        var t = SanitizeForDisplay(text, int.MaxValue);
        if (t.Length == 0) return Array.Empty<string>();

        return t.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
