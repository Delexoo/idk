using System.Windows.Controls;
using System.Windows.Media;

namespace Idk.Services;

/// <summary>App-wide typography: Inter (UI/body), Plus Jakarta Sans (headings).</summary>
public static class AppTypography
{
    public const string BodyPackUri = "pack://application:,,,/Assets/Fonts/#Inter";
    public const string DisplayPackUri = "pack://application:,,,/Assets/Fonts/#Plus Jakarta Sans";
    public const string SymbolFamily = "Segoe MDL2 Assets, Segoe UI Symbol";
    public const string MonoFamily = "Cascadia Mono, Consolas, Courier New";

    public static System.Windows.Media.FontFamily Body { get; } = EmojiTextHelper.BodyWithEmojiFallback;
    public static System.Windows.Media.FontFamily Display { get; } = new(DisplayPackUri);
    public static System.Windows.Media.FontFamily Symbol { get; } = new(SymbolFamily);
    public static System.Windows.Media.FontFamily Mono { get; } = new(MonoFamily);

    public static void ApplyBody(System.Windows.Controls.Control control) => control.FontFamily = Body;

    public static void ApplyDisplay(System.Windows.Controls.Control control) => control.FontFamily = Display;

    public static void ApplyBody(TextBlock block)
    {
        block.FontFamily = Body;
        EmojiTextHelper.ApplyToChatTextBlock(block);
    }

    public static void ApplyDisplay(TextBlock block) => block.FontFamily = Display;
}
