using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Idk.Services;

/// <summary>Render emoji with Windows color emoji fonts (not hollow Segoe UI Symbol fallbacks).</summary>
public static class EmojiTextHelper
{
    /// <summary>Segoe Fluent Emoji (Win11+) then Segoe UI Emoji — both support color glyphs.</summary>
    public static System.Windows.Media.FontFamily ColorEmojiFont { get; } = new("Segoe Fluent Emoji, Segoe UI Emoji");

    private static readonly Regex EmojiRx = new(
        @"\p{Extended_Pictographic}(?:\p{Emoji_Modifier}|\uFE0F|[\u200D\p{Extended_Pictographic}])*",
        RegexOptions.Compiled);

    public static System.Windows.Media.FontFamily BodyWithEmojiFallback { get; } = new(
        new Uri("pack://application:,,,/"),
        "./Assets/Fonts/#Inter, Segoe Fluent Emoji, Segoe UI Emoji");

    public static void ApplyToChatTextBlock(TextBlock block)
    {
        block.FontFamily = BodyWithEmojiFallback;
        TextOptions.SetTextFormattingMode(block, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(block, TextRenderingMode.Auto);
    }

    public static void AppendRuns(InlineCollection inlines, string text, Action<Run>? configure = null)
    {
        if (string.IsNullOrEmpty(text)) return;

        var last = 0;
        foreach (Match match in EmojiRx.Matches(text))
        {
            if (match.Index > last)
                AddRun(inlines, text.Substring(last, match.Index - last), emoji: false, configure);

            AddRun(inlines, match.Value, emoji: true, configure);
            last = match.Index + match.Length;
        }

        if (last < text.Length)
            AddRun(inlines, text[last..], emoji: false, configure);
    }

    private static void AddRun(InlineCollection inlines, string text, bool emoji, Action<Run>? configure)
    {
        if (text.Length == 0) return;

        var run = new Run(text);
        if (emoji)
            run.FontFamily = ColorEmojiFont;

        configure?.Invoke(run);
        inlines.Add(run);
    }
}
