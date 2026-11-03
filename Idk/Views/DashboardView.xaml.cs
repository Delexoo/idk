using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Idk.Services;

namespace Idk.Views;

public partial class DashboardView : System.Windows.Controls.UserControl
{
    private ContextMenu? _chatHistoryContextMenu;
    private DashboardChatRow? _detailRow;
    private string _detailSelection = "";
    private string _detailQuestion = "";
    private string _detailAnswer = "";
    private CancellationTokenSource? _chatFollowUpCts;
    private ChatHistoryEntryDto? _detailEntry;
    private UIElement? _streamingRow;
    private TextBlock? _streamingAssistantBody;
    private System.Windows.Shapes.Ellipse[]? _typingDots;
    private DispatcherTimer? _typingTimer;
    private int _typingFrame;
    private bool _suppressHistorySelection;
    private bool _openBillingWhenLoaded;

    private Window? ShellOwner => Window.GetWindow(this);

    public DashboardView()
    {
        InitializeComponent();
        EnsureChatHistoryContextMenu();
        HistoryList.PreviewMouseRightButtonDown += HistoryList_PreviewMouseRightButtonDown;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        LicenseService.Changed += OnLicenseChanged;
        FreeAskQuota.Changed += OnLicenseChanged;
        ChatHistoryStore.ChatHistoryChanged += OnChatHistoryFromOverlay;
        ThemeManager.ThemeChanged += OnThemeChanged;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ChatHistoryStore.ChatHistoryChanged -= OnChatHistoryFromOverlay;
        LicenseService.Changed -= OnLicenseChanged;
        FreeAskQuota.Changed -= OnLicenseChanged;
        ThemeManager.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(OnThemeChanged);
            return;
        }

        Background = ThemeManager.Brush("AppBg");
        Foreground = ThemeManager.Brush("AppFg");
        RefreshActivePanelForTheme();
    }

    private void RefreshActivePanelForTheme()
    {
        var onDashboard = ScrollDashboard.Visibility == Visibility.Visible;
        var onSettings = ScrollSettings.Visibility == Visibility.Visible;
        var onHowToUse = ScrollHowToUse.Visibility == Visibility.Visible;
        var onChat = ScrollChatDetail.Visibility == Visibility.Visible;

        SetSidebarNav(
            dashboardActive: onDashboard,
            settingsActive: onSettings,
            howToUseActive: onHowToUse);

        if (onDashboard)
        {
            RefreshDashboard();
        }
        else if (onSettings)
        {
            Refresh();
        }
        else if (onHowToUse)
        {
            SyncHowToUseBody();
        }
        else if (onChat && _detailRow != null)
        {
            ShowChatDetail(_detailRow);
        }

        RefreshSidebarHistoryForTheme();
    }

    private void RefreshSidebarHistoryForTheme()
    {
        if (HistoryList.ItemsSource == null) return;

        var selected = HistoryList.SelectedItem;
        var items = HistoryList.ItemsSource;
        HistoryList.ItemsSource = null;
        HistoryList.ItemsSource = items;

        if (selected != null)
            HistoryList.SelectedItem = selected;
    }

    private void OnChatHistoryFromOverlay() =>
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            RefreshChatHistory();
            RefreshDashboard();
        }));

    private void SmoothScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is ScrollViewer sv)
            SmoothScrollHelper.OnPreviewMouseWheel(sv, e);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
    {
        Refresh();
        RefreshChatHistory();
            RefreshDashboard();
            if (_openBillingWhenLoaded)
                ShowBillingView();
            else
                ShowDashboardView();
            if (OpenRouterService.IsReady)
                _ = ChatTitleService.RefreshStaleTitlesAsync();
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, "DashboardView.OnLoaded");
            throw;
        }
    }

    private void ShowError(string message)
    {
        Error.Text = message;
        ErrorBanner.Visibility = Visibility.Visible;
    }

    private void ShortcutsInfo_Click(object sender, RoutedEventArgs e) =>
        ToggleShortcutsOverlay();

    private void ShortcutsOverlayClose_Click(object sender, RoutedEventArgs e) =>
        CloseShortcutsOverlay();

    private void ShortcutsOverlayBackdrop_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == sender)
            CloseShortcutsOverlay();
    }

    private void ShortcutsPanel_MouseDown(object sender, MouseButtonEventArgs e) =>
        e.Handled = true;

    private void ToggleShortcutsOverlay() =>
        ShortcutsOverlay.Visibility = ShortcutsOverlay.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;

    private void CloseShortcutsOverlay() =>
        ShortcutsOverlay.Visibility = Visibility.Collapsed;

    private void ScreenCaptureToggle_Click(object sender, RoutedEventArgs e)
    {
        SnapDashSwitchKnob(ScreenCaptureToggle, ScreenCaptureToggle.IsChecked == true);
        SaveAllSettingsFromUi();
    }

    private void LaunchLogin_Click(object sender, RoutedEventArgs e)
    {
        SnapDashSwitchKnob(LaunchLogin, LaunchLogin.IsChecked == true);
        SaveAllSettingsFromUi();
    }

    private void HowToUseNav_Click(object sender, RoutedEventArgs e) => ShowHowToUseView();

    private void HideFromTaskbarToggle_Click(object sender, RoutedEventArgs e)
    {
        SnapDashSwitchKnob(HideFromTaskbarToggle, HideFromTaskbarToggle.IsChecked == true);
        SaveAllSettingsFromUi();
    }

    private void HideFromScreenShareToggle_Click(object sender, RoutedEventArgs e)
    {
        SnapDashSwitchKnob(HideFromScreenShareToggle, HideFromScreenShareToggle.IsChecked == true);
        SaveAllSettingsFromUi();
    }

    private bool _syncingSelectionModeUi;
    private bool _syncingAskBarPlacementUi;
    private bool _syncingThemeUi;

    private void SelectionModeChoice_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncingSelectionModeUi) return;
        if (sender is not System.Windows.Controls.RadioButton { IsChecked: true }) return;

        var mode = ReferenceEquals(sender, SelectionModeInstant)
            ? SelectionActivationMode.Instant
            : SelectionActivationMode.ShiftSelect;

        SaveAllSettingsFromUi();
    }

    private void AskBarPlacementChoice_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncingAskBarPlacementUi) return;
        if (sender is not System.Windows.Controls.RadioButton { IsChecked: true }) return;
        SaveAllSettingsFromUi();
    }

    private void ThemeModeChoice_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncingThemeUi) return;
        if (sender is not System.Windows.Controls.RadioButton { IsChecked: true }) return;
        SaveAllSettingsFromUi();
    }

    private void SyncThemeModeUi()
    {
        var light = SettingsStore.AppTheme == AppTheme.Light;
        _syncingThemeUi = true;
        try
        {
            ThemeModeDark.IsChecked = !light;
            ThemeModeLight.IsChecked = light;
        }
        finally
        {
            _syncingThemeUi = false;
        }
    }

    private void SyncSelectionModeLabel()
    {
        var instant = SettingsStore.SelectionMode == SelectionActivationMode.Instant;
        var shift = !instant;
        _syncingSelectionModeUi = true;
        try
        {
            SelectionModeShift.IsChecked = shift;
            SelectionModeInstant.IsChecked = instant;
        }
        finally
        {
            _syncingSelectionModeUi = false;
        }

        SelectWithoutShiftDesc.Text = SettingsStore.GetSelectionModeDescription();
    }

    private void SyncAskBarPlacementUi()
    {
        var bottom = SettingsStore.AskBarPlacement == AskBarPlacementMode.BottomCenter;
        _syncingAskBarPlacementUi = true;
        try
        {
            AskBarFollowCursor.IsChecked = !bottom;
            AskBarBottomCenter.IsChecked = bottom;
        }
        finally
        {
            _syncingAskBarPlacementUi = false;
        }

        AskBarPlacementDesc.Text = SettingsStore.GetAskBarPlacementDescription();
    }

    private void DashboardNav_Click(object sender, RoutedEventArgs e) => ShowDashboardView();

    private void BillingNav_Click(object sender, RoutedEventArgs e) => ShowBillingView();

    private void SettingsNav_Click(object sender, RoutedEventArgs e) => ShowSettingsView();

    public void ShowBillingFromOutside()
    {
        if (!IsLoaded)
        {
            _openBillingWhenLoaded = true;
            return;
        }

        ShowBillingView();
    }

    private void OpenBilling_Click(object sender, RoutedEventArgs e) => ShowBillingView();

    private void HistoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressHistorySelection) return;
        if (HistoryList.SelectedItem is ChatHistorySectionDivider)
        {
            _suppressHistorySelection = true;
            HistoryList.SelectedItem = e.RemovedItems.OfType<DashboardChatRow>().FirstOrDefault();
            _suppressHistorySelection = false;
            return;
        }

        if (HistoryList.SelectedItem is not DashboardChatRow row)
        {
            ShowDashboardView();
            return;
        }

        ShowChatDetail(row);
    }

    private void DashboardPinnedList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DashboardPinnedList.SelectedItem is not DashboardChatRow row) return;
        ShowChatDetail(row);
        _suppressHistorySelection = true;
        HistoryList.SelectedItem = ChatHistoryStore.ReadRecentForSidebar(100)
            .OfType<DashboardChatRow>()
            .FirstOrDefault(r => r.UtcTicks == row.UtcTicks);
        _suppressHistorySelection = false;
        DashboardPinnedList.SelectedItem = null;
    }

    private void DashboardActivityList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DashboardActivityList.SelectedItem is not DashboardChatRow row) return;
        _suppressHistorySelection = true;
        HistoryList.SelectedItem = HistoryList.Items.Cast<object>()
            .OfType<DashboardChatRow>()
            .FirstOrDefault(r => r.UtcTicks == row.UtcTicks);
        _suppressHistorySelection = false;
        ShowChatDetail(row);
        _suppressHistorySelection = true;
        DashboardActivityList.SelectedItem = null;
        _suppressHistorySelection = false;
    }

    private void HideAllMainPanels()
    {
        ScrollDashboard.Visibility = Visibility.Collapsed;
        ScrollBilling.Visibility = Visibility.Collapsed;
        ScrollSettings.Visibility = Visibility.Collapsed;
        ScrollHowToUse.Visibility = Visibility.Collapsed;
        ScrollChatDetail.Visibility = Visibility.Collapsed;
    }

    private void ShowDashboardView()
    {
        CancelChatFollowUp();
        _detailRow = null;
        _detailEntry = null;
        HideAllMainPanels();
        ScrollDashboard.Visibility = Visibility.Visible;
        SetSidebarNav(dashboardActive: true);
        _suppressHistorySelection = true;
        HistoryList.SelectedItem = null;
        _suppressHistorySelection = false;
        RefreshDashboard();
    }

    private void ShowBillingView()
    {
        CancelChatFollowUp();
        _detailRow = null;
        _detailEntry = null;
        HideAllMainPanels();
        ScrollBilling.Visibility = Visibility.Visible;
        SetSidebarNav(billingActive: true);
        _suppressHistorySelection = true;
        HistoryList.SelectedItem = null;
        _suppressHistorySelection = false;
        RefreshLicenseUi();
    }

    private void ShowSettingsView()
    {
        CancelChatFollowUp();
        _detailRow = null;
        _detailEntry = null;
        HideAllMainPanels();
        ScrollSettings.Visibility = Visibility.Visible;
        SetSidebarNav(settingsActive: true);
        _suppressHistorySelection = true;
        HistoryList.SelectedItem = null;
        _suppressHistorySelection = false;
    }

    private void ShowHowToUseView()
    {
        CancelChatFollowUp();
        _detailRow = null;
        _detailEntry = null;
        HideAllMainPanels();
        ScrollHowToUse.Visibility = Visibility.Visible;
        SyncHowToUseBody();
        SetSidebarNav(howToUseActive: true);
        _suppressHistorySelection = true;
        HistoryList.SelectedItem = null;
        _suppressHistorySelection = false;
    }

    private void SyncHowToUseBody()
    {
        var instant = SettingsStore.SelectionMode == SelectionActivationMode.Instant;
        var openLine = instant
            ? "Highlight any text — the ask bar opens right away."
            : "Hold Shift, then highlight text — the ask bar appears near your selection.";

        HowToUseTryPrompt.Text = SettingsStore.SelectionMode switch
        {
            SelectionActivationMode.Instant =>
                "Highlight a word below and ask what it means. Instant mode opens the bar on its own.",
            SelectionActivationMode.CustomHotkey when SettingsStore.SelectionHotkeyVk > 0 =>
                $"Hold {SettingsStore.FormatHotkey(SettingsStore.SelectionHotkeyVk, SettingsStore.SelectionHotkeyModifiers)}, highlight a word below, and ask what it means.",
            _ =>
                "Hold Shift, highlight a word below, and ask what it means."
        };

        HowToUseBody.Text =
            "1. You get 5 free asks. Upgrade in Billing when you want idk Pro, which is $3 a month for this computer.\n\n" +
            "2. Choose how idk opens: Shift + select (default) or Instant in Selection mode.\n\n" +
            "3. " + openLine + "\n\n" +
            "4. Type a question in the ask bar, or press Ctrl+Enter for a quick “What does this mean?”\n\n" +
            "5. Read the answer in the card, or open past chats from the dashboard sidebar.\n\n" +
            "6. Press Esc or click away to dismiss the ask bar. idk keeps running in the tray.";
    }

    private void ShowChatDetail(DashboardChatRow row)
    {
        if (!ChatHistoryStore.TryGetEntry(row.UtcTicks, out var entry) || entry == null)
        {
            ShowDashboardView();
            return;
        }

        _detailRow = row;
        _detailEntry = entry;
        var (selection, question, answer) = ChatHistoryStore.GetDisplayParts(entry);
        _detailSelection = selection;
        _detailQuestion = question;
        _detailAnswer = answer;
        ChatDetailTitle.Text = row.TitleDisplay.TrimStart('↑', ' ');
        ChatDetailWhen.Text = row.WhenLabel;
        ChatQuickPinBtn.Content = row.IsPinned ? "Unpin" : "Pin";
        RenderChatThread();
        ChatFollowUpInput.Clear();
        SyncChatFollowUpChrome();

        HideAllMainPanels();
        ScrollChatDetail.Visibility = Visibility.Visible;
        SetSidebarNav();
    }

    private void RenderChatThread(bool scrollToEnd = true)
    {
        StopTypingAnimation();
        _streamingRow = null;
        _streamingAssistantBody = null;
        ChatMessagesPanel.Children.Clear();

        if (_detailEntry == null)
        {
            ChatMessagesPanel.Children.Add(CreateEmptyThreadHint());
            if (scrollToEnd) ScrollChatToEnd();
            return;
        }

        var messages = ChatThreadParser.ParseEntry(_detailEntry);
        if (messages.Count == 0)
        {
            ChatMessagesPanel.Children.Add(CreateEmptyThreadHint());
            if (scrollToEnd) ScrollChatToEnd();
            return;
        }

        foreach (var msg in messages)
            ChatMessagesPanel.Children.Add(CreateMessageBubble(msg));

        if (scrollToEnd) ScrollChatToEnd();
    }

    private UIElement CreateEmptyThreadHint() =>
        new TextBlock
        {
            Text = "No messages in this chat yet.",
            FontFamily = AppTypography.Body,
            Foreground = (System.Windows.Media.Brush)FindResource("DashMuted"),
            FontSize = 13,
            Margin = new Thickness(0, 24, 0, 0)
        };

    private UIElement CreateMessageBubble(ChatMessageVm msg) => msg.Kind switch
    {
        ChatMessageKind.Context => CreateContextBubble(msg.Text),
        ChatMessageKind.User => CreateUserBubble(msg.Text),
        ChatMessageKind.Assistant => CreateAssistantBubble(msg.Text, msg.IsStreaming).container,
        _ => new Border()
    };

    private UIElement CreateContextBubble(string text)
    {
        var preview = SelectionSnippetFormatter.ToPreview(text, 160);
        var quote = new TextBlock
        {
            Text = preview,
            FontFamily = AppTypography.Body,
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 36,
            LineHeight = 16,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            Foreground = (System.Windows.Media.Brush)FindResource("DashSubtle"),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Margin = new Thickness(2, 0, 36, 16),
            ToolTip = SelectionSnippetFormatter.ToPreview(text, 480)
        };
        quote.Typography.Variants = FontVariants.Superscript;
        return quote;
    }

    private UIElement CreateUserBubble(string text)
    {
        var body = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            Foreground = ThemeManager.Brush("DashOnAccent"),
            LineHeight = 22
        };
        AppTypography.ApplyBody(body);
        EmojiTextHelper.AppendRuns(body.Inlines, text);

        var bubble = new Border
        {
            Background = (System.Windows.Media.Brush)FindResource("DashAccentBlue"),
            CornerRadius = new CornerRadius(18, 18, 4, 18),
            Padding = new Thickness(14, 10, 14, 10),
            MaxWidth = 520,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Child = body
        };

        return new Grid
        {
            Margin = new Thickness(64, 0, 0, 16),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Children = { bubble }
        };
    }

    private StackPanel CreateTypingDotsPanel()
    {
        _typingDots = new System.Windows.Shapes.Ellipse[3];
        var panel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        var fill = ThemeManager.Brush("DashTypingDot");
        for (var i = 0; i < 3; i++)
        {
            _typingDots[i] = new System.Windows.Shapes.Ellipse
            {
                Width = 7,
                Height = 7,
                Margin = new Thickness(i == 0 ? 0 : 6, 0, 0, 0),
                Fill = fill,
                Opacity = i == 0 ? 1.0 : 0.35
            };
            panel.Children.Add(_typingDots[i]);
        }

        return panel;
    }

    private void StartTypingAnimation()
    {
        StopTypingAnimation();
        if (_typingDots == null) return;
        _typingFrame = 0;
        _typingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(340) };
        _typingTimer.Tick += (_, _) =>
        {
            if (_typingDots == null) return;
            var active = _typingFrame % 3;
            for (var i = 0; i < 3; i++)
                _typingDots[i].Opacity = i == active ? 1.0 : 0.35;
            _typingFrame++;
        };
        _typingTimer.Start();
    }

    private void StopTypingAnimation()
    {
        _typingTimer?.Stop();
        _typingTimer = null;
        _typingDots = null;
    }

    private (UIElement container, TextBlock? body) CreateAssistantBubble(string text, bool streaming = false)
    {
        UIElement bubbleContent;
        TextBlock? body = null;

        if (streaming && string.IsNullOrWhiteSpace(text))
        {
            bubbleContent = CreateTypingDotsPanel();
        }
        else
        {
            body = new TextBlock
            {
                FontFamily = AppTypography.Body,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14,
                Foreground = (System.Windows.Media.Brush)FindResource("DashInk"),
                LineHeight = 24
            };
            if (!string.IsNullOrWhiteSpace(text))
                MarkdownAnswerFormatter.ApplyDisplayMarkdown(body, text);
            bubbleContent = body;
        }

        var bubble = new Border
        {
            Background = ThemeManager.Brush("DashChatBubbleAssistantBg"),
            BorderBrush = ThemeManager.Brush("DashChatBubbleAssistantBorder"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4, 18, 18, 18),
            Padding = new Thickness(16, 14, 16, 14),
            MinHeight = streaming && body == null ? 40 : 0,
            MaxWidth = 560,
            Child = bubbleContent
        };

        var avatar = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(16),
            ClipToBounds = true,
            BorderBrush = ThemeManager.Brush("DashChatBubbleAssistantBorder"),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Top,
            Background = new ImageBrush
            {
                ImageSource = new System.Windows.Media.Imaging.BitmapImage(
                    new Uri("pack://application:,,,/Assets/idk.png")),
                Stretch = Stretch.UniformToFill
            }
        };

        var label = new TextBlock
        {
            Text = "idk",
            FontFamily = AppTypography.Display,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = (System.Windows.Media.Brush)FindResource("DashSubtle"),
            Margin = new Thickness(0, 0, 0, 6)
        };

        var col = new StackPanel();
        col.Children.Add(label);
        col.Children.Add(bubble);

        var row = new Grid { Margin = new Thickness(0, 0, 0, 20) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(avatar, 0);
        Grid.SetColumn(col, 1);
        avatar.Margin = new Thickness(0, 2, 12, 0);
        row.Children.Add(avatar);
        row.Children.Add(col);

        return (row, body);
    }

    private void AppendUserBubble(string text) =>
        ChatMessagesPanel.Children.Add(CreateUserBubble(text));

    private void BeginAssistantStreamingBubble()
    {
        var (container, body) = CreateAssistantBubble("", streaming: true);
        _streamingRow = container;
        _streamingAssistantBody = body;
        ChatMessagesPanel.Children.Add(container);
        StartTypingAnimation();
    }

    private void PromoteStreamingToTextBubble(string streamText)
    {
        if (_streamingRow == null) return;
        StopTypingAnimation();
        var idx = ChatMessagesPanel.Children.IndexOf(_streamingRow);
        if (idx < 0) return;

        var (container, body) = CreateAssistantBubble(streamText);
        ChatMessagesPanel.Children.RemoveAt(idx);
        ChatMessagesPanel.Children.Insert(idx, container);
        _streamingRow = container;
        _streamingAssistantBody = body;
    }

    private void UpdateStreamingAssistantBubble(string streamText)
    {
        if (string.IsNullOrWhiteSpace(streamText)) return;

        if (_streamingAssistantBody == null && _streamingRow != null)
            PromoteStreamingToTextBubble(streamText);
        else if (_streamingAssistantBody != null)
            MarkdownAnswerFormatter.ApplyDisplayMarkdown(_streamingAssistantBody, streamText);
    }

    private void ScrollChatToEnd() =>
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => ChatThreadScroller.ScrollToEnd());

    private void SyncChatFollowUpChrome()
    {
        var ready = OpenRouterService.IsReady && FreeAskQuota.CanAsk;
        ChatFollowUpInput.IsEnabled = ready;
        ChatFollowUpHint.Text = !OpenRouterService.IsReady
            ? "Model access is not built into this copy."
            : FreeAskQuota.BlockedMessage;
        ChatFollowUpHint.Visibility = ready ? Visibility.Collapsed : Visibility.Visible;
        ChatFollowUpUpgrade.Visibility = OpenRouterService.IsReady && !FreeAskQuota.CanAsk
            ? Visibility.Visible
            : Visibility.Collapsed;
        ChatFollowUpError.Visibility = Visibility.Collapsed;
        if (!ready)
            ChatFollowUpLoading.Visibility = Visibility.Collapsed;
        UpdateChatFollowUpPlaceholderVisibility();
    }

    private void UpdateChatFollowUpPlaceholderVisibility() =>
        ChatFollowUpPlaceholder.Visibility =
            ChatFollowUpInput.IsEnabled &&
            string.IsNullOrWhiteSpace(ChatFollowUpInput.Text) &&
            !ChatFollowUpInput.IsFocused
                ? Visibility.Visible
                : Visibility.Collapsed;

    private void ChatFollowUpInput_TextChanged(object sender, TextChangedEventArgs e) =>
        UpdateChatFollowUpPlaceholderVisibility();

    private void ChatFollowUpInput_GotFocus(object sender, RoutedEventArgs e) =>
        UpdateChatFollowUpPlaceholderVisibility();

    private void ChatFollowUpInput_LostFocus(object sender, RoutedEventArgs e) =>
        UpdateChatFollowUpPlaceholderVisibility();

    private void CancelChatFollowUp()
    {
        _chatFollowUpCts?.Cancel();
        _chatFollowUpCts = null;
        StopTypingAnimation();
    }

    private void ChatFollowUpInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;
        e.Handled = true;
        _ = RunChatFollowUpAsync();
    }

    private void ChatFollowUpAsk_Click(object sender, RoutedEventArgs e) =>
        _ = RunChatFollowUpAsync();

    private async Task RunChatFollowUpAsync()
    {
        if (_detailRow is not { } row) return;

        var followUp = ChatFollowUpInput.Text.Trim();
        if (followUp.Length == 0) return;

        if (!FreeAskQuota.CanAsk)
        {
            ChatFollowUpError.Text = FreeAskQuota.BlockedMessage;
            ChatFollowUpError.Visibility = Visibility.Visible;
            ChatFollowUpUpgrade.Visibility = Visibility.Visible;
            return;
        }

        if (!OpenRouterService.IsReady)
        {
            ChatFollowUpError.Text = "Model access is not built into this copy.";
            ChatFollowUpError.Visibility = Visibility.Visible;
            return;
        }

        CancelChatFollowUp();
        _chatFollowUpCts = new CancellationTokenSource();
        var ct = _chatFollowUpCts.Token;

        ChatFollowUpError.Visibility = Visibility.Collapsed;
        ChatFollowUpLoading.Visibility = Visibility.Collapsed;
        ChatFollowUpInput.IsEnabled = false;

        var turns = _detailEntry != null
            ? ChatHistoryStore.GetThreadMessages(_detailEntry)
            : Array.Empty<ChatThreadMessageDto>();
        var userMsg = ChatPromptBuilder.BuildFollowUpUserMessage(_detailSelection, turns, followUp);
        var streamAnswer = "";

        AppendUserBubble(followUp);
        BeginAssistantStreamingBubble();

        try
        {
            await foreach (var delta in OpenRouterService.StreamChatAsync(userMsg, visionPngBase64: null, ct))
            {
                if (ct.IsCancellationRequested) break;
                streamAnswer = AnswerMetaStripper.StripLeadingMeta(streamAnswer + delta);
                UpdateStreamingAssistantBubble(streamAnswer);
            }

            if (ct.IsCancellationRequested || streamAnswer.Trim().Length == 0)
            {
                RenderChatThread();
                return;
            }

            FreeAskQuota.RecordSuccess();
            ChatHistoryStore.AppendThreadTurn(row.UtcTicks, followUp, streamAnswer.Trim());
            _ = ChatTitleService.RefreshTitleForEntryAsync(row.UtcTicks);
            if (ChatHistoryStore.TryGetEntry(row.UtcTicks, out var updated) && updated != null)
            {
                _detailEntry = updated;
                var parts = ChatHistoryStore.GetDisplayParts(updated);
                _detailAnswer = parts.Answer;
            }

            RenderChatThread();
            ChatFollowUpInput.Clear();
            RefreshChatHistory();
        }
        catch (OperationCanceledException)
        {
            RenderChatThread();
        }
        catch (Exception ex)
        {
            ChatFollowUpError.Text = ex.Message;
            ChatFollowUpError.Visibility = Visibility.Visible;
            ChatFollowUpUpgrade.Visibility = ex.Message == FreeAskQuota.BlockedMessage
                ? Visibility.Visible
                : Visibility.Collapsed;
            RenderChatThread();
        }
        finally
        {
            StopTypingAnimation();
            ChatFollowUpLoading.Visibility = Visibility.Collapsed;
            SyncChatFollowUpChrome();
            _chatFollowUpCts = null;
        }
    }

    private void SetSidebarNav(
        bool dashboardActive = false,
        bool billingActive = false,
        bool settingsActive = false,
        bool howToUseActive = false)
    {
        DashboardNav.IsChecked = dashboardActive;
        BillingNav.IsChecked = billingActive;
        SettingsNav.IsChecked = settingsActive;
        HowToUseNav.IsChecked = howToUseActive;
    }

    public void RefreshDashboard()
    {
        try
        {
            RefreshDashboardCore();
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, "RefreshDashboard");
        }
    }

    private void RefreshDashboardCore()
    {
        var insights = ChatHistoryStore.GetInsights();
        var pinned = insights.PinnedHighlights;
        var hasPinned = pinned.Count > 0;
        DashboardPinnedHeader.Visibility = hasPinned ? Visibility.Visible : Visibility.Collapsed;
        DashboardPinnedList.Visibility = hasPinned ? Visibility.Visible : Visibility.Collapsed;
        DashboardPinnedList.ItemsSource = pinned;

        var activity = ChatHistoryStore.ReadRecentForUi(max: 8);
        DashboardActivityList.ItemsSource = activity;
        var hasActivity = activity.Count > 0;
        DashboardActivityList.Visibility = hasActivity ? Visibility.Visible : Visibility.Collapsed;
        DashboardActivityEmpty.Visibility = hasActivity ? Visibility.Collapsed : Visibility.Visible;
        DashboardActivityEmpty.Text = hasActivity
            ? ""
            : $"No asks yet. {SettingsStore.GetEmptyStateHint()}";
    }

    private const double DashSwitchKnobTravel = 20;

    private static TranslateTransform? GetDashSwitchKnobTransform(ToggleButton tb)
    {
        tb.ApplyTemplate();
        return tb.Template?.FindName("KnobT", tb) as TranslateTransform;
    }

    private static void SnapDashSwitchKnob(ToggleButton tb, bool isOn)
    {
        if (GetDashSwitchKnobTransform(tb) is not { } tt) return;
        tt.BeginAnimation(TranslateTransform.XProperty, null);
        tt.X = isOn ? DashSwitchKnobTravel : 0;
    }

    private void SaveAllSettingsFromUi()
    {
        ErrorBanner.Visibility = Visibility.Collapsed;
        try
        {
            var mode = SelectionModeInstant.IsChecked == true
                ? SelectionActivationMode.Instant
                : SelectionActivationMode.ShiftSelect;
            var theme = ThemeModeLight.IsChecked == true ? AppTheme.Light : AppTheme.Dark;
            var askBar = AskBarBottomCenter.IsChecked == true
                ? AskBarPlacementMode.BottomCenter
                : AskBarPlacementMode.FollowCursor;

            SettingsStore.SaveDashboard(new DashboardSaveDto
            {
                OpenAtLogin = LaunchLogin.IsChecked == true,
                ScreenCaptureEnabled = ScreenCaptureToggle.IsChecked == true,
                HideFromTaskbar = HideFromTaskbarToggle.IsChecked == true,
                HideFromScreenShare = HideFromScreenShareToggle.IsChecked == true,
                SelectionMode = mode,
                Theme = theme,
                AskBarPlacement = askBar
            });

            ThemeManager.Apply(theme);
            if (ShellOwner is { } shell)
                SettingsStore.ApplyMainWindowTaskbarVisibility(shell);

            SyncSelectionModeLabel();
            SyncAskBarPlacementUi();
            SyncThemeModeUi();
            Refresh();
            if (ScrollDashboard.Visibility == Visibility.Visible)
                RefreshDashboard();
            global::Idk.App.NotifyOverlaySettingsChanged();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    public void FlushPersistForShellClose()
    {
        try
        {
            SaveAllSettingsFromUi();
        }
        catch
        {
            /* ignore flush errors */
        }
    }

    private void Refresh()
    {
        var s = SettingsStore.ReadDashboard();
        LaunchLogin.IsChecked = s.OpenAtLogin;
        ScreenCaptureToggle.IsChecked = s.ScreenCaptureEnabled;
        HideFromTaskbarToggle.IsChecked = s.HideFromTaskbar;
        HideFromScreenShareToggle.IsChecked = s.HideFromScreenShare;
        SnapDashSwitchKnob(ScreenCaptureToggle, ScreenCaptureToggle.IsChecked == true);
        SnapDashSwitchKnob(LaunchLogin, LaunchLogin.IsChecked == true);
        SnapDashSwitchKnob(HideFromTaskbarToggle, HideFromTaskbarToggle.IsChecked == true);
        SnapDashSwitchKnob(HideFromScreenShareToggle, HideFromScreenShareToggle.IsChecked == true);
        SyncSelectionModeLabel();
        SyncAskBarPlacementUi();
        SyncThemeModeUi();
        if (Window.GetWindow(this) is { } hostWin)
            SettingsStore.ApplyMainWindowTaskbarVisibility(hostWin);
        ErrorBanner.Visibility = Visibility.Collapsed;
        RefreshLicenseUi();
    }

    private void OnLicenseChanged()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(OnLicenseChanged);
            return;
        }

        RefreshLicenseUi();
        if (ScrollDashboard.Visibility == Visibility.Visible)
            RefreshDashboard();
    }

    private void RefreshLicenseUi()
    {
        var licensed = LicenseService.IsLicensed;
        var status = string.IsNullOrWhiteSpace(LicenseService.StatusMessage)
            ? LicenseService.PriceMessage
            : LicenseService.StatusMessage;
        LicenseStatusText.Text = status;
        var showStatus = !licensed
            && !string.Equals(status, LicenseService.PriceMessage, StringComparison.Ordinal)
            && !string.Equals(status, FreeAskQuota.Summary, StringComparison.Ordinal);
        DashboardLicenseStatusText.Text = status;
        DashboardLicenseStatusText.Visibility = showStatus ? Visibility.Visible : Visibility.Collapsed;
        BillingFreeAsksText.Text = FreeAskQuota.Summary;
        DashboardFreeAsksText.Text = FreeAskQuota.Summary;
        SettingsPlanSummary.Text = FreeAskQuota.Summary;
        LicenseVersionText.Text = "idk " + AppVersion.Current;
        var buy = licensed ? Visibility.Collapsed : Visibility.Visible;
        LicenseBuyButton.Visibility = buy;
        LicenseManageButton.Visibility = licensed ? Visibility.Visible : Visibility.Collapsed;
        DashboardLicenseBuyButton.Visibility = buy;
        var enabled = !LicenseService.IsBusy;
        LicenseBuyButton.IsEnabled = enabled;
        LicenseManageButton.IsEnabled = enabled;
        LicenseCheckButton.IsEnabled = enabled;
        DashboardLicenseBuyButton.IsEnabled = enabled;
    }

    private async void LicenseBuy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await LicenseService.BuyAsync();
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, "LicenseBuy");
        }
        finally
        {
            RefreshLicenseUi();
        }
    }

    private async void LicenseManage_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await LicenseService.ManageAsync();
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, "LicenseManage");
        }
        finally
        {
            RefreshLicenseUi();
        }
    }

    private async void LicenseCheck_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await LicenseService.CheckAsync();
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, "LicenseCheck");
        }
        finally
        {
            RefreshLicenseUi();
        }
    }

    public void RefreshChatHistory()
    {
        var selectedTicks = (HistoryList.SelectedItem as DashboardChatRow)?.UtcTicks;
        var items = ChatHistoryStore.ReadRecentForSidebar(max: 100);
        HistoryList.ItemsSource = items;
        var has = items.OfType<DashboardChatRow>().Any();
        HistoryList.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        HistoryEmpty.Visibility = has ? Visibility.Collapsed : Visibility.Visible;

        var rows = items.OfType<DashboardChatRow>().ToList();
        if (selectedTicks is long ticks && rows.Any(r => r.UtcTicks == ticks))
        {
            _suppressHistorySelection = true;
            HistoryList.SelectedItem = rows.First(r => r.UtcTicks == ticks);
            _suppressHistorySelection = false;
            if (HistoryList.SelectedItem is DashboardChatRow row)
                ShowChatDetail(row);
        }
        else if (ScrollChatDetail.Visibility == Visibility.Visible)
        {
            ShowDashboardView();
        }
        else if (ScrollDashboard.Visibility == Visibility.Visible)
        {
            RefreshDashboard();
        }
    }

    private void EnsureChatHistoryContextMenu()
    {
        if (_chatHistoryContextMenu != null) return;
        var cm = new ContextMenu();
        cm.SetResourceReference(FrameworkElement.StyleProperty, "DashHistoryContextMenu");
        cm.Opened += HistoryContextMenu_Opened;

        void AddItem(string header, RoutedEventHandler click, bool destructive = false)
        {
            var mi = new MenuItem { Header = header };
            if (destructive)
                mi.SetResourceReference(FrameworkElement.StyleProperty, "DashHistoryMenuItemDelete");
            else
                mi.SetResourceReference(FrameworkElement.StyleProperty, "DashHistoryMenuItem");
            mi.Click += click;
            cm.Items.Add(mi);
        }

        void AddSep()
        {
            var sep = new Separator();
            sep.SetResourceReference(FrameworkElement.StyleProperty, "DashHistoryMenuSeparator");
            cm.Items.Add(sep);
        }

        AddItem("Copy", HistoryCopy_Click);
        AddSep();
        var pin = new MenuItem { Tag = "PinHeader", Header = "Pin to top" };
        pin.SetResourceReference(FrameworkElement.StyleProperty, "DashHistoryMenuItem");
        pin.Click += HistoryPinToggle_Click;
        cm.Items.Add(pin);
        AddItem("Rename…", HistoryRename_Click);
        AddItem("Export to file…", HistoryExport_Click);
        AddItem("Open chats folder", HistoryOpenFolder_Click);
        AddSep();
        AddItem("Delete", HistoryDelete_Click, destructive: true);

        _chatHistoryContextMenu = cm;
    }

    private void OpenHistoryMenuForRow(DashboardChatRow row, UIElement placementTarget, bool atMouse = false)
    {
        EnsureChatHistoryContextMenu();
        _chatHistoryContextMenu!.DataContext = row;
        _chatHistoryContextMenu.PlacementTarget = placementTarget;
        _chatHistoryContextMenu.Placement = atMouse ? PlacementMode.MousePoint : PlacementMode.Bottom;
        _chatHistoryContextMenu.IsOpen = true;
    }

    private void HistoryMoreButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        OpenHistoryMenuFromMoreButton(sender);
    }

    private void HistoryMoreButton_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        OpenHistoryMenuFromMoreButton(sender, atMouse: true);
    }

    private void OpenHistoryMenuFromMoreButton(object sender, bool atMouse = false)
    {
        if (sender is not System.Windows.Controls.Button btn) return;
        if (btn.DataContext is not DashboardChatRow row) return;
        OpenHistoryMenuForRow(row, btn, atMouse);
    }

    private static bool IsClickOnMoreButton(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is System.Windows.Controls.Button) return true;
            if (source is ListBoxItem) return false;
            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private void HistoryList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.ListBox) return;
        if (IsClickOnMoreButton(e.OriginalSource as DependencyObject)) return;

        var dep = e.OriginalSource as DependencyObject;
        while (dep != null && dep is not ListBoxItem)
            dep = VisualTreeHelper.GetParent(dep);
        if (dep is not ListBoxItem lbi) return;
        if (lbi.DataContext is ChatHistorySectionDivider) return;
        if (lbi.DataContext is not DashboardChatRow row) return;

        e.Handled = true;
        OpenHistoryMenuForRow(row, lbi, atMouse: true);
    }

    private void ChatQuickCopy_Click(object sender, RoutedEventArgs e)
    {
        if (_detailRow is not { } row) return;
        HistoryCopy_Click(WrapRowMenuItem(row), e);
    }

    private void ChatQuickPin_Click(object sender, RoutedEventArgs e)
    {
        if (_detailRow is not { } row) return;
        HistoryPinToggle_Click(WrapRowMenuItem(row), e);
    }

    private void ChatQuickExport_Click(object sender, RoutedEventArgs e)
    {
        if (_detailRow is not { } row) return;
        HistoryExport_Click(WrapRowMenuItem(row), e);
    }

    private void ChatQuickDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_detailRow is not { } row) return;
        HistoryDelete_Click(WrapRowMenuItem(row), e);
    }

    private static MenuItem WrapRowMenuItem(DashboardChatRow row) =>
        new() { DataContext = row };

    private void HistoryContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu cm) return;
        if (cm.DataContext is not DashboardChatRow row) return;
        foreach (var o in cm.Items)
        {
            if (o is MenuItem mi && mi.Tag is string t && t == "PinHeader")
            {
                mi.Header = row.IsPinned ? "Unpin from top" : "Pin to top";
                return;
            }
        }
    }

    private static DashboardChatRow? HistoryRowFromMenuItem(object sender) =>
        (sender as MenuItem)?.DataContext as DashboardChatRow;

    private static (string Selection, string Question, string Answer) HistoryDisplayParts(DashboardChatRow row)
    {
        if (ChatHistoryStore.TryGetEntry(row.UtcTicks, out var e) && e != null)
            return ChatHistoryStore.GetDisplayParts(e);
        return ("", row.Title, row.Preview == "—" ? "" : row.Preview);
    }

    private static string FormatChatForClipboard(DashboardChatRow row)
    {
        var parts = HistoryDisplayParts(row);
        var body = new StringBuilder();
        body.AppendLine(row.Title);
        body.AppendLine(row.WhenLabel);
        body.AppendLine();

        if (ChatHistoryStore.TryGetEntry(row.UtcTicks, out var entry) && entry != null)
        {
            foreach (var msg in ChatThreadParser.ParseEntry(entry))
            {
                var label = msg.Kind switch
                {
                    ChatMessageKind.Context => "[Context]",
                    ChatMessageKind.User => "You",
                    _ => "idk"
                };
                body.AppendLine($"{label}:");
                body.AppendLine(msg.Text);
                body.AppendLine();
            }
        }

        return body.ToString().TrimEnd();
    }

    private void HistoryCopy_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryRowFromMenuItem(sender) is not { } row) return;
        try
        {
            System.Windows.Clipboard.SetText(FormatChatForClipboard(row));
        }
        catch
        {
            /* ignore clipboard */
        }
    }

    private void HistoryPinToggle_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryRowFromMenuItem(sender) is not { } row) return;
        ChatHistoryStore.SetPinned(row.UtcTicks, !row.IsPinned);
        if (_detailRow?.UtcTicks == row.UtcTicks)
            ChatQuickPinBtn.Content = !row.IsPinned ? "Unpin" : "Pin";
        RefreshChatHistory();
    }

    private void HistoryRename_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryRowFromMenuItem(sender) is not { } row) return;
        if (!TryPromptRename(row.Title, out var newTitle)) return;
        ChatHistoryStore.TryRename(row.UtcTicks, newTitle);
    }

    private void HistoryExport_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryRowFromMenuItem(sender) is not { } row) return;
        var safe = SanitizeFileName(row.Title);
        if (safe.Length == 0) safe = "chat";
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export chat",
            Filter = "Text file (*.txt)|*.txt|All files|*.*",
            DefaultExt = ".txt",
            FileName = safe + ".txt",
            AddExtension = true
        };
        if (dlg.ShowDialog(ShellOwner) != true) return;
        var text = new StringBuilder();
        var parts = HistoryDisplayParts(row);
        text.AppendLine(row.Title);
        text.AppendLine(row.WhenLabel);
        if (!string.IsNullOrWhiteSpace(parts.Selection))
        {
        text.AppendLine();
            text.AppendLine("Selected:");
            text.AppendLine(parts.Selection);
        }
        text.AppendLine();
        text.AppendLine("Question:");
        text.AppendLine(parts.Question);
        text.AppendLine();
        text.AppendLine("Response:");
        text.AppendLine(parts.Answer);
        try
        {
            File.WriteAllText(dlg.FileName, text.ToString(), Encoding.UTF8);
        }
        catch (Exception ex)
        {
            ModernDialog.Alert(ShellOwner, "Export failed", ex.Message);
        }
    }

    private void HistoryOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dir = SettingsStore.DataDirectory;
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ModernDialog.Alert(ShellOwner, "Could not open folder", ex.Message);
        }
    }

    private void HistoryDelete_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryRowFromMenuItem(sender) is not { } row) return;
        ChatHistoryStore.RemoveByUtcTicks(row.UtcTicks);
        if (ScrollChatDetail.Visibility == Visibility.Visible
            && HistoryList.SelectedItem is DashboardChatRow sel
            && sel.UtcTicks == row.UtcTicks)
        {
            ShowDashboardView();
        }
    }

    private void DeleteAllChats_Click(object sender, RoutedEventArgs e)
    {
        if (ChatHistoryStore.GetStats().TotalAsks == 0)
        {
            ModernDialog.Alert(ShellOwner, "No chats", "There are no saved chats to delete.");
            return;
        }

        if (!ModernDialog.Confirm(
                ShellOwner,
                "Delete all chats?",
                "This permanently removes every saved chat from this PC. This cannot be undone.",
                confirmText: "Delete all",
                destructive: true))
            return;

        ChatHistoryStore.ClearAll();
        if (ScrollChatDetail.Visibility == Visibility.Visible)
            ShowDashboardView();
        RefreshChatHistory();
        RefreshDashboard();
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = (name ?? "").Where(c => !invalid.Contains(c)).ToArray();
        var s = new string(chars).Trim();
        return s.Length > 120 ? s[..120] : s;
    }

    private bool TryPromptRename(string currentTitle, out string newTitle)
    {
        newTitle = "";
        var tb = new System.Windows.Controls.TextBox
        {
            Text = currentTitle,
            Margin = new Thickness(16, 12, 16, 0),
            FontSize = 14,
            MaxLength = 200
        };
        var ok = new System.Windows.Controls.Button { Content = "Save", IsDefault = true, MinWidth = 88, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new System.Windows.Controls.Button { Content = "Cancel", IsCancel = true, MinWidth = 88 };
        var buttons = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(16, 16, 16, 12),
            Children = { ok, cancel }
        };
        var root = new System.Windows.Controls.StackPanel { Children = { tb, buttons } };
        var dlg = new Window
        {
            Title = "Rename chat",
            Content = root,
            Width = 440,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = ShellOwner,
            Background = ThemeManager.Brush("DashMenuBg"),
            Foreground = ThemeManager.Brush("DashInk"),
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false
        };
        ok.Click += (_, _) => { dlg.DialogResult = true; dlg.Close(); };
        cancel.Click += (_, _) => { dlg.DialogResult = false; dlg.Close(); };
        dlg.Loaded += (_, _) =>
        {
            tb.Focus();
            tb.SelectAll();
        };
        if (dlg.ShowDialog() != true) return false;
        newTitle = tb.Text.Trim();
        return newTitle.Length > 0;
    }
}
