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

    public static UIElement CreateUserBubble(string text)
    {
        var body = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12.5,
            LineHeight = 17
        };
        body.SetResourceReference(TextBlock.ForegroundProperty, "DashOnAccent");
        AppTypography.ApplyBody(body);
        EmojiTextHelper.AppendRuns(body.Inlines, text);

        var bubble = new Border
        {
            CornerRadius = new CornerRadius(12, 12, 3, 12),
            Padding = new Thickness(9, 5, 9, 5),
            MaxWidth = 240,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Child = body
        };
        bubble.SetResourceReference(Border.BackgroundProperty, "DashAccentBlue");

        var row = new Grid
        {
            Margin = new Thickness(18, 0, 0, 6),
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
                FontSize = 12.5,
                LineHeight = 17
            };
            body.SetResourceReference(TextBlock.ForegroundProperty, "OverlayAssistantInk");
            if (!string.IsNullOrWhiteSpace(text))
                MarkdownAnswerFormatter.ApplyDisplayMarkdown(body, text);
            else
                EmojiTextHelper.ApplyToChatTextBlock(body);
            bubbleContent = body;
        }

        var bubble = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3, 12, 12, 12),
            Padding = new Thickness(9, 6, 9, 6),
            MinHeight = streaming && body == null ? 28 : 0,
            MaxWidth = 260,
            Child = bubbleContent
        };
        bubble.SetResourceReference(Border.BackgroundProperty, "OverlayAssistantBg");
        bubble.SetResourceReference(Border.BorderBrushProperty, "OverlayAssistantBorder");

        var avatar = new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(9),
            ClipToBounds = true,
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Top,
            Background = new ImageBrush
            {
                ImageSource = new BitmapImage(new Uri("pack://application:,,,/Assets/idk.png")),
                Stretch = Stretch.UniformToFill
            }
        };
        avatar.SetResourceReference(Border.BorderBrushProperty, "OverlayAssistantBorder");

        var label = new TextBlock
        {
            Text = "idk",
            FontFamily = AppTypography.Display,
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 3)
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "OverlayLabelMuted");

        var col = new StackPanel();
        col.Children.Add(label);
        col.Children.Add(bubble);

        var row = new Grid
        {
            Margin = new Thickness(0, 0, 0, 8),
            Tag = TagAssistant
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(avatar, 0);
        Grid.SetColumn(col, 1);
        avatar.Margin = new Thickness(0, 1, 6, 0);
        row.Children.Add(avatar);
        row.Children.Add(col);

        return (row, body);
    }

    public static StackPanel CreateTypingDots()
    {
        var panel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        for (var i = 0; i < 3; i++)
        {
            var dot = new Ellipse
            {
                Width = 5,
                Height = 5,
                Margin = new Thickness(i == 0 ? 0 : 4, 0, 0, 0),
                Opacity = i == 0 ? 1.0 : 0.35
            };
            dot.SetResourceReference(Shape.FillProperty, "OverlayTypingDot");
            panel.Children.Add(dot);
        }

        return panel;
    }
}
