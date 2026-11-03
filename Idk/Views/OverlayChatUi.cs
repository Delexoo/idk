using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Idk.Services;

namespace Idk.Views;

/// <summary>Chat bubbles for the overlay answer card (compact dashboard-style thread).</summary>
internal static class OverlayChatUi
{
    public const string TagUser = "user";
    public const string TagAssistant = "assistant";

    private static readonly SolidColorBrush UserBg = Freeze(0x25, 0x63, 0xEB);
    private static readonly SolidColorBrush AssistantBg = Freeze(0x14, 0x14, 0x13);
    private static readonly SolidColorBrush AssistantBorder = Freeze(0x2E, 0x2E, 0x2C);
    private static readonly SolidColorBrush AssistantInk = Freeze(0xDC, 0xDC, 0xDC);
    private static readonly SolidColorBrush LabelMuted = Freeze(0x73, 0x73, 0x73);
    private static readonly SolidColorBrush DotFill = Freeze(0x8E, 0x8E, 0x8E);

    public static UIElement CreateUserBubble(string text)
    {
        var body = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            Foreground = System.Windows.Media.Brushes.White,
            LineHeight = 20
        };
        AppTypography.ApplyBody(body);
        EmojiTextHelper.AppendRuns(body.Inlines, text);

        var bubble = new Border
        {
            Background = UserBg,
            CornerRadius = new CornerRadius(14, 14, 3, 14),
            Padding = new Thickness(11, 8, 11, 8),
            MaxWidth = 220,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Child = body
        };

        var row = new Grid
        {
            Margin = new Thickness(28, 0, 0, 10),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Tag = TagUser
        };
        row.Children.Add(bubble);
        return row;
    }

    public static (UIElement row, TextBlock? body) CreateAssistantBubble(string text, bool streaming = false)
    {
        UIElement bubbleContent;
        TextBlock? body = null;

        if (streaming && string.IsNullOrWhiteSpace(text))
            bubbleContent = CreateTypingDots();
        else
        {
            body = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                Foreground = AssistantInk,
                LineHeight = 20
            };
            if (!string.IsNullOrWhiteSpace(text))
                MarkdownAnswerFormatter.ApplyDisplayMarkdown(body, text);
            else
                EmojiTextHelper.ApplyToChatTextBlock(body);
            bubbleContent = body;
        }

        var bubble = new Border
        {
            Background = AssistantBg,
            BorderBrush = AssistantBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3, 14, 14, 14),
            Padding = new Thickness(12, 10, 12, 10),
            MinHeight = streaming && body == null ? 36 : 0,
            MaxWidth = 248,
            Child = bubbleContent
        };

        var avatar = new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(13),
            ClipToBounds = true,
            BorderBrush = AssistantBorder,
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Top,
            Background = new ImageBrush
            {
                ImageSource = new BitmapImage(new Uri("pack://application:,,,/Assets/idk.png")),
                Stretch = Stretch.UniformToFill
            }
        };

        var label = new TextBlock
        {
            Text = "idk",
            FontFamily = AppTypography.Display,
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = LabelMuted,
            Margin = new Thickness(0, 0, 0, 5)
        };

        var col = new StackPanel();
        col.Children.Add(label);
        col.Children.Add(bubble);

        var row = new Grid
        {
            Margin = new Thickness(0, 0, 0, 12),
            Tag = TagAssistant
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(avatar, 0);
        Grid.SetColumn(col, 1);
        avatar.Margin = new Thickness(0, 2, 8, 0);
        row.Children.Add(avatar);
        row.Children.Add(col);

        return (row, body);
    }

    public static StackPanel CreateTypingDots()
    {
        var panel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        for (var i = 0; i < 3; i++)
        {
            panel.Children.Add(new Ellipse
            {
                Width = 6,
                Height = 6,
                Margin = new Thickness(i == 0 ? 0 : 5, 0, 0, 0),
                Fill = DotFill,
                Opacity = i == 0 ? 1.0 : 0.35
            });
        }

        return panel;
    }

    private static SolidColorBrush Freeze(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
