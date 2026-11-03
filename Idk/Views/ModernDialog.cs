using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Idk.Services;

namespace Idk.Views;

/// <summary>Dark, minimal dialogs that match the idk dashboard (use sparingly).</summary>
public static class ModernDialog
{
    private static readonly System.Windows.Media.Color Ink = System.Windows.Media.Color.FromRgb(0xF0, 0xEF, 0xED);
    private static readonly System.Windows.Media.Color Muted = System.Windows.Media.Color.FromRgb(0x8B, 0x89, 0x85);
    private static readonly System.Windows.Media.Color CardBg = System.Windows.Media.Color.FromRgb(0x14, 0x14, 0x13);
    private static readonly System.Windows.Media.Color CardBorder = System.Windows.Media.Color.FromRgb(0x35, 0x34, 0x31);
    private static readonly System.Windows.Media.Color Accent = System.Windows.Media.Color.FromRgb(0x25, 0x63, 0xEB);
    private static readonly System.Windows.Media.Color AccentDark = System.Windows.Media.Color.FromRgb(0x1D, 0x4E, 0xD8);
    private static readonly System.Windows.Media.Color DangerBg = System.Windows.Media.Color.FromRgb(0x3F, 0x1A, 0x1A);
    private static readonly System.Windows.Media.Color DangerBorder = System.Windows.Media.Color.FromRgb(0x7F, 0x1D, 0x1D);
    private static readonly System.Windows.Media.Color DangerFg = System.Windows.Media.Color.FromRgb(0xFC, 0xA5, 0xA5);

    public static void Alert(Window? owner, string title, string message) =>
        Show(owner, title, message, confirmText: "OK", showCancel: false);

    public static bool Confirm(
        Window? owner,
        string title,
        string message,
        string confirmText = "Confirm",
        string cancelText = "Cancel",
        bool destructive = false) =>
        Show(owner, title, message, confirmText, cancelText, showCancel: true, destructive);

    private static bool Show(
        Window? owner,
        string title,
        string message,
        string confirmText,
        string cancelText = "Cancel",
        bool showCancel = true,
        bool destructive = false)
    {
        var confirmed = false;

        var titleBlock = new TextBlock
        {
            Text = title,
            FontFamily = AppTypography.Display,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Ink),
            TextWrapping = TextWrapping.Wrap
        };

        var messageBlock = new TextBlock
        {
            Text = message,
            FontFamily = AppTypography.Body,
            FontSize = 14,
            Foreground = new SolidColorBrush(Muted),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 22,
            Margin = new Thickness(0, 8, 0, 0)
        };

        var confirmBtn = CreateButton(confirmText, primary: !destructive, destructive);
        confirmBtn.IsDefault = true;
        confirmBtn.MinWidth = 88;

        var buttonRow = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 20, 0, 0)
        };

        var dlg = new Window
        {
            Title = title,
            Width = 400,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = owner,
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x0A, 0x0A, 0x0A)),
            Foreground = new SolidColorBrush(Ink),
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false
        };

        if (showCancel)
        {
            var cancelBtn = CreateButton(cancelText, primary: false, destructive: false);
            cancelBtn.IsCancel = true;
            cancelBtn.MinWidth = 88;
            cancelBtn.Margin = new Thickness(0, 0, 8, 0);
            cancelBtn.Click += (_, _) => { confirmed = false; dlg.Close(); };
            buttonRow.Children.Add(cancelBtn);
        }

        confirmBtn.Click += (_, _) => { confirmed = true; dlg.Close(); };
        buttonRow.Children.Add(confirmBtn);

        dlg.Content = new Border
        {
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x0A, 0x0A, 0x0A)),
            Padding = new Thickness(16),
            Child = new Border
            {
                Background = new SolidColorBrush(CardBg),
                BorderBrush = new SolidColorBrush(CardBorder),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(22, 20, 22, 20),
                Child = new StackPanel
                {
                    Children = { titleBlock, messageBlock, buttonRow }
                }
            }
        };

        WindowsRoundedWindowCorners.TryApply(dlg, smallRadius: true);
        dlg.ShowDialog();
        return confirmed;
    }

    private static System.Windows.Controls.Button CreateButton(string label, bool primary, bool destructive)
    {
        var bg = primary ? Accent : destructive ? DangerBg : System.Windows.Media.Color.FromRgb(0x22, 0x22, 0x20);
        var border = primary ? AccentDark : destructive ? DangerBorder : CardBorder;
        var fg = destructive && !primary ? DangerFg : Ink;

        var btn = new System.Windows.Controls.Button
        {
            Content = label,
            FontFamily = AppTypography.Body,
            Padding = new Thickness(14, 8, 14, 8),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Cursor = System.Windows.Input.Cursors.Hand,
            Foreground = new SolidColorBrush(primary ? System.Windows.Media.Colors.White : fg),
            Background = new SolidColorBrush(bg),
            BorderBrush = new SolidColorBrush(border),
            BorderThickness = new Thickness(1)
        };

        return btn;
    }
}
