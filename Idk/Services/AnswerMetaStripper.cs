using System.Text.RegularExpressions;

namespace Idk.Services;

/// <summary>Remove model planning / strategy preamble so answers start immediately.</summary>
public static class AnswerMetaStripper
{
    private static readonly Regex MetaLineRx = new(
        @"^\s*(?:\*\*)?(?:Strategy|Plan|Approach|Thinking|Reasoning|Method|Analysis|Outline)(?:\*\*)?\s*:\s*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ThinkingTagRx = new(
        @"^\s*<think(?:ing)?>\s*[\s\S]*?</think(?:ing)?>\s*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string StripLeadingMeta(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";

        var t = raw.Replace("\r\n", "\n").TrimStart();

        for (var guard = 0; guard < 12; guard++)
        {
            var before = t;

            t = ThinkingTagRx.Replace(t, "");
            t = t.TrimStart();

            if (t.StartsWith("```", StringComparison.Ordinal))
            {
                var close = t.IndexOf("```", 3, StringComparison.Ordinal);
                if (close > 0)
                {
                    var fenceBody = t[3..close].TrimStart();
                    if (MetaLineRx.IsMatch(fenceBody) || fenceBody.StartsWith("thinking", StringComparison.OrdinalIgnoreCase))
                        t = t[(close + 3)..].TrimStart();
                }
            }

            var lines = t.Split('\n');
            var i = 0;
            while (i < lines.Length && string.IsNullOrWhiteSpace(lines[i]))
                i++;

            if (i < lines.Length && MetaLineRx.IsMatch(lines[i]))
            {
                i++;
                while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]) && !MetaLineRx.IsMatch(lines[i]))
                    i++;
                while (i < lines.Length && string.IsNullOrWhiteSpace(lines[i]))
                    i++;
                t = i < lines.Length ? string.Join("\n", lines[i..]) : "";
                continue;
            }

            if (before == t) break;
        }

        return t.TrimStart();
    }
}
