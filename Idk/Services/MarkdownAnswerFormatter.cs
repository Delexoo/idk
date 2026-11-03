using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace Idk.Services;

/// <summary>Render assistant markdown as readable WPF inlines: bold, bullets, spacing.</summary>
public static class MarkdownAnswerFormatter
{
    private static readonly Regex HeadingRx = new(@"^#{1,6}\s+", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex LineBulletRx = new(@"^\s*[-*+]\s+", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex NumberedListRx = new(@"^\s*(\d+)\.\s+", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex DoubleUnderscoreBoldRx = new(@"__([^_]+)__", RegexOptions.Compiled);
    private static readonly Regex StrikethroughRx = new(@"~~([^~]+)~~", RegexOptions.Compiled);
    private static readonly Regex BlockQuoteRx = new(@"^>\s*", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex InlineCodeRx = new(@"`([^`]+)`", RegexOptions.Compiled);
    private static readonly Regex LinkRx = new(@"\[([^\]]+)\]\([^)]+\)", RegexOptions.Compiled);
    private static readonly Regex HorizontalRuleRx = new(@"^\s*([-*_])\1{2,}\s*$", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex ItalicAsteriskRx = new(@"(?<![\*\u2022])\*([^*\n]+?)\*(?!\*)", RegexOptions.Compiled);
    private static readonly Regex BoldSectionRx = new(@"\*\*[^*\n]+:\*\*", RegexOptions.Compiled);

    public static string SanitizeForDisplay(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        var t = AnswerMetaStripper.StripLeadingMeta(raw).Replace("\r\n", "\n");

        t = StrikethroughRx.Replace(t, "$1");
        t = DoubleUnderscoreBoldRx.Replace(t, "**$1**");
        t = HeadingRx.Replace(t, "");
        t = BlockQuoteRx.Replace(t, "");
        t = HorizontalRuleRx.Replace(t, "");

        // Section headers like **In research:** on their own line
        t = Regex.Replace(t, @"(\*\*[^*\n]+:\*\*)", "\n\n$1\n");

        // Bullets glued to headers or each other: "**Header:** - item" or "sentence. - item"
        t = Regex.Replace(t, @"(\*\*[^*\n]+:\*\*)\s+-\s+", "$1\n• ");
        t = Regex.Replace(t, @"(?<=[.!?])\s+-\s+(?=[""'])", "\n• ");
        t = Regex.Replace(t, @"(?<=\S)\s+-\s+(?=[""'])", "\n• ");
        t = Regex.Replace(t, @"(?<=\S)\s+-\s+", "\n• ");

        t = LineBulletRx.Replace(t, "• ");
        t = NumberedListRx.Replace(t, "$1. ");

        t = InlineCodeRx.Replace(t, "$1");
        t = LinkRx.Replace(t, "$1");
        t = ItalicAsteriskRx.Replace(t, "$1");

        t = Regex.Replace(t, @"[ \t]+\n", "\n");
        t = Regex.Replace(t, @"\n[ \t]+", "\n");
        t = Regex.Replace(t, @"\n{3,}", "\n\n");

        return t.Trim();
    }

    public static void ApplyDisplayMarkdown(TextBlock target, string? markdown)
    {
        var cleaned = SanitizeForDisplay(markdown);
        ApplyFormattedText(target, cleaned);
    }

    public static void ApplyFormattedText(TextBlock target, string? text)
    {
        target.Inlines.Clear();
        EmojiTextHelper.ApplyToChatTextBlock(target);
        if (string.IsNullOrEmpty(text)) return;

        var lines = text.Split('\n');
        var paraGap = false;

        for (var li = 0; li < lines.Length; li++)
        {
            var line = lines[li];
            if (string.IsNullOrWhiteSpace(line))
            {
                paraGap = true;
                continue;
            }

            var trimmed = line.Trim();

            if (target.Inlines.Count > 0)
            {
                target.Inlines.Add(new LineBreak());
                if (paraGap || IsBoldSectionLine(trimmed))
                    target.Inlines.Add(new LineBreak());
            }

            paraGap = false;
            AppendLineInlines(target, trimmed);
        }
    }

    private static bool IsBoldSectionLine(string trimmed) =>
        BoldSectionRx.IsMatch(trimmed) && trimmed.StartsWith("**", StringComparison.Ordinal);

    private static void AppendLineInlines(TextBlock target, string line)
    {
        if (line.StartsWith("• ", StringComparison.Ordinal))
        {
            target.Inlines.Add(new Run("      • ")
            {
                FontWeight = FontWeights.SemiBold
            });
            AppendBoldRuns(target, line[2..].TrimStart());
            return;
        }

        var numMatch = Regex.Match(line, @"^(\d+)\.\s+(.*)$");
        if (numMatch.Success)
        {
            target.Inlines.Add(new Run($"    {numMatch.Groups[1].Value}. ")
            {
                FontWeight = FontWeights.SemiBold
            });
            AppendBoldRuns(target, numMatch.Groups[2].Value);
            return;
        }

        AppendBoldRuns(target, line);
    }

    private static void AppendBoldRuns(TextBlock target, string line)
    {
        var parts = line.Split("**", StringSplitOptions.None);
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0) continue;
            var bold = i % 2 == 1;
            EmojiTextHelper.AppendRuns(
                target.Inlines,
                parts[i],
                run =>
                {
                    if (bold)
                        run.FontWeight = FontWeights.SemiBold;
                });
        }
    }
}
