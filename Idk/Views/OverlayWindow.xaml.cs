using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Idk.Services;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;

namespace Idk.Views;

public partial class OverlayWindow : Window
{
    private const double PillW = 128;
    private const double PillH = 31;
    private const double PillMinW = 118;
    private const double PillMinH = 29;
    private const double PillCornerRadius = 15.5;
    private const double CardCornerRadius = 10;
    private const double CardW = 312;
    private const double CardH = 336;
    private const double CardMinW = 236;
    private const double CardMinH = 188;

    private string _selectionText = "";
    private Rect _selectionBoundsPhysical;
    private Point _cursorFallbackPhysical;
    private AutomationElement? _selectionSourceElement;
    private string? _programName;
    private string _lastUserMessage = "";
    private string _lastAnswer = "";
    private readonly List<ChatThreadMessageDto> _overlayThreadTurns = new();
    private bool _historyRecordEnabled;
    private string? _historyTitleThisTurn;
    private long? _historyUtcTicks;
    private CancellationTokenSource? _streamCts;
    private bool _hasMorphedToCard;
    private DispatcherTimer? _selectionWatchTimer;
    private EventHandler? _selectionWatchTickHandler;
    private DateTime _selectionWatchGraceUntil = DateTime.MinValue;
    private EventHandler? _followRenderingHandler;

    private const double MouseFollowGapBelow = 42;
    private const string QuickMeaningQuestion = SettingsStore.DefaultQueryPlaceholder;

    private MouseFollowPillLayout.PillFollowSmoothState _pillFollowSmooth;
    private UIElement? _streamingRow;
    private TextBlock? _streamingAssistantBody;
    private System.Windows.Shapes.Ellipse[]? _overlayTypingDots;
    private DispatcherTimer? _overlayTypingTimer;
    private int _overlayTypingFrame;

    private bool IsCardStreaming => _streamingRow != null;

    public OverlayWindow()
    {
        InitializeComponent();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                EscapeOrDismiss();
                return;
            }

            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (!IsVisible || PillPanel.Visibility != Visibility.Visible) return;
                e.Handled = true;
                _ = SubmitAsync(QuickMeaningQuestion);
            }
        };
        SourceInitialized += OnOverlaySourceInitialized;
        Loaded += OnOverlayLoaded;
        Unloaded += OnOverlayUnloaded;
        MouseEnter += (_, _) => UpdateGlassChrome();
        MouseLeave += (_, _) => UpdateGlassChrome();
        AddHandler(Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnKeyboardFocusChanged), true);
        AddHandler(Keyboard.LostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnKeyboardFocusChanged), true);
        ShellOuter.SizeChanged += (_, _) => SyncShellChromeShape();
        SizeChanged += (_, _) => SyncShellChromeShape();
        DpiChanged += (_, _) => SyncShellChromeShape();
        ThemeManager.ThemeChanged += OnAppThemeChanged;
    }

    private void OnAppThemeChanged() => ApplyTheme();

    private void OnOverlaySourceInitialized(object? sender, EventArgs e)
    {
        _ = new WindowInteropHelper(this).EnsureHandle();
        WindowsRoundedWindowCorners.TryApply(this, smallRadius: true, suppressDwmBorder: true);
        GlobalEscapeHotkey.Attach(this);
    }

    private void OnKeyboardFocusChanged(object sender, KeyboardFocusChangedEventArgs e) => UpdateGlassChrome();

    private void AnswerScroll_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e) =>
        SmoothScroll.HandlePreviewMouseWheel(sender as ScrollViewer, e, 0.2);

    public void RefreshChromeSettings()
    {
        ApplyTheme();
        SyncQueryPlaceholder();
    }

    public void ApplyTheme()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(ApplyTheme);
            return;
        }

        var windowBg = ThemeManager.Brush("OverlayWindowBg");
        Background = windowBg;
        RootContentGrid.Background = windowBg;
        ShellCornerFill.Background = ThemeManager.Brush("OverlayChromeIdle");
        QueryBox.Foreground = ThemeManager.Brush("OverlayQueryText");
        QueryBox.CaretBrush = ThemeManager.Brush("OverlayQueryCaret");
        QueryBox.SelectionBrush = ThemeManager.Brush("OverlayQuerySelection");
        QueryPlaceholder.Foreground = ThemeManager.Brush("OverlayQueryPlaceholder");
        FollowUpBox.Foreground = ThemeManager.Brush("OverlayQueryText");
        FollowUpBox.CaretBrush = ThemeManager.Brush("OverlayQueryCaret");
        FollowUpBox.SelectionBrush = ThemeManager.Brush("OverlayQuerySelection");
        FollowUpPlaceholder.Foreground = ThemeManager.Brush("OverlayFollowUpPlaceholder");
        ErrorText.Foreground = ThemeManager.Brush("OverlayErrorFg");

        CardFollowUpBar.Background = ThemeManager.Brush("OverlayCardFooterBg");
        CardFollowUpBar.BorderBrush = ThemeManager.Brush("OverlayCardDivider");
        CardHeaderBar.BorderBrush = ThemeManager.Brush("OverlayCardDivider");

        UpdateGlassChrome();
    }

    private void OnOverlayLoaded(object sender, RoutedEventArgs e)
    {
        ApplyTheme();
        SyncQueryPlaceholder();
        UpdatePlaceholderVisibility();
        SyncShellChromeShape();
    }

    private void SyncQueryPlaceholder() =>
        QueryPlaceholder.Text = SettingsStore.GetQueryPlaceholder();

    private void OnOverlayUnloaded(object sender, RoutedEventArgs e)
    {
        ThemeManager.ThemeChanged -= OnAppThemeChanged;
        StopSelectionWatch();
        StopMouseFollow();
        GlobalEscapeHotkey.Detach();
        ClearNativeWindowRegion();
    }

    private void UpdateGlassChrome()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(UpdateGlassChrome);
            return;
        }

        ShellOuter.Effect = null;
        ShellOuter.Opacity = 1;
        var hover = !SettingsStore.IsOverlaySolidOnly && (IsMouseOver || IsKeyboardFocusWithin);
        var idle = ThemeManager.Brush("OverlayChromeIdle");
        var hoverBrush = ThemeManager.Brush("OverlayChromeHover");
        ShellOuter.Background = hover ? hoverBrush : idle;
        ShellOuter.BorderBrush = System.Windows.Media.Brushes.Transparent;
        ShellOuter.BorderThickness = new Thickness(0);
        ShellCornerFill.Background = hover ? hoverBrush : idle;
    }

    /// <summary>
    /// Border.CornerRadius only affects the border's own fill; children still paint a rectangular box.
    /// Clip to a matching rounded rect so the pill/card reads as rounded, and keep the underlay corners in sync.
    /// </summary>
    private void SyncShellChromeShape(bool syncNativeRegion = true)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => SyncShellChromeShape(syncNativeRegion));
            return;
        }

        var cr = ShellOuter.CornerRadius;
        ShellCornerFill.CornerRadius = cr;

        var w = ShellOuter.ActualWidth;
        var h = ShellOuter.ActualHeight;
        if (w < 1 || h < 1)
        {
            ShellOuter.Clip = null;
            ShellVisualRoot.Clip = null;
            RootContentGrid.Clip = null;
            ClearNativeWindowRegion();
            return;
        }

        var rx = Math.Max(0, cr.TopLeft);
        var ry = Math.Max(0, cr.TopLeft);
        rx = Math.Min(rx, w / 2);
        ry = Math.Min(ry, h / 2);
        var shellClip = new RectangleGeometry(new Rect(0, 0, w, h), rx, ry);
        ShellOuter.Clip = shellClip;

        // Clip the scaled enter-animation host so scaled corners never show a square silhouette.
        var vw = ShellVisualRoot.ActualWidth > 1 ? ShellVisualRoot.ActualWidth : w;
        var vh = ShellVisualRoot.ActualHeight > 1 ? ShellVisualRoot.ActualHeight : h;
        var vr = Math.Min(rx, vw / 2);
        ShellVisualRoot.Clip = new RectangleGeometry(new Rect(0, 0, vw, vh), vr, vr);

        // Clip at the window root so transparent outer pixels match the pill/card radius (reduces "rectangle" look).
        var rw = RootContentGrid.ActualWidth > 1 ? RootContentGrid.ActualWidth : ActualWidth;
        var rh = RootContentGrid.ActualHeight > 1 ? RootContentGrid.ActualHeight : ActualHeight;
        if (rw >= 1 && rh >= 1)
        {
            var rr = Math.Min(rx, Math.Min(rw / 2, rh / 2));
            RootContentGrid.Clip = new RectangleGeometry(new Rect(0, 0, rw, rh), rr, rr);
        }
        else
            RootContentGrid.Clip = null;

        if (syncNativeRegion)
            SyncNativeWindowRegion();
    }

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    /// <summary>
    /// WPF clips visuals to rounded corners, but the HWND still had a rectangular hit-test box.
    /// Match the client area to a rounded region so transparent corners pass clicks through.
    /// </summary>
    private void SyncNativeWindowRegion()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        if (ActualWidth < 1 || ActualHeight < 1)
        {
            SetWindowRgn(hwnd, IntPtr.Zero, true);
            return;
        }

        var m = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        var wPix = Math.Max(1, (int)Math.Ceiling(ActualWidth * m.M11));
        var hPix = Math.Max(1, (int)Math.Ceiling(ActualHeight * m.M22));

        var rDip = ShellOuter.CornerRadius.TopLeft;
        if (rDip < 0.5)
        {
            SetWindowRgn(hwnd, IntPtr.Zero, true);
            return;
        }

        var maxRDip = Math.Min(ActualWidth, ActualHeight) / 2.0;
        var r = Math.Min(rDip, maxRDip);
        var ellW = (int)Math.Clamp(Math.Round(2 * r * m.M11), 2, wPix);
        var ellH = (int)Math.Clamp(Math.Round(2 * r * m.M22), 2, hPix);

        var hrgn = CreateRoundRectRgn(0, 0, wPix, hPix, ellW, ellH);
        if (hrgn == IntPtr.Zero) return;

        if (SetWindowRgn(hwnd, hrgn, true) == 0)
            DeleteObject(hrgn);
    }

    private void ClearNativeWindowRegion()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
            SetWindowRgn(hwnd, IntPtr.Zero, true);
    }

    private void StartSelectionWatch()
    {
        StopSelectionWatch();
        _selectionWatchTickHandler = (_, _) => SelectionWatch_OnTick();
        _selectionWatchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(280) };
        _selectionWatchTimer.Tick += _selectionWatchTickHandler;
        _selectionWatchTimer.Start();
    }

    private void StopSelectionWatch()
    {
        if (_selectionWatchTimer == null) return;
        if (_selectionWatchTickHandler != null)
            _selectionWatchTimer.Tick -= _selectionWatchTickHandler;
        _selectionWatchTimer.Stop();
        _selectionWatchTimer = null;
        _selectionWatchTickHandler = null;
    }

    private void StartMouseFollow()
    {
        StopMouseFollow();
        _followRenderingHandler = OnFollowRendering;
        CompositionTarget.Rendering += _followRenderingHandler;
    }

    private void StopMouseFollow()
    {
        if (_followRenderingHandler != null)
        {
            CompositionTarget.Rendering -= _followRenderingHandler;
            _followRenderingHandler = null;
        }

        _pillFollowSmooth = default;
    }

    private void OnFollowRendering(object? sender, EventArgs e)
    {
        if (!IsVisible || _hasMorphedToCard)
        {
            StopMouseFollow();
            return;
        }

        if (IsMouseOver) return;
        PositionFollowMouse();
    }

    /// <summary>Pill mode: centered on the cursor horizontally, just below it (no selection nudge — follows pointer).</summary>
    private void PositionFollowMouse(double? widthOverride = null)
    {
        if (_hasMorphedToCard || !IsVisible) return;

        var w = widthOverride ?? (ActualWidth > 1 ? ActualWidth : PillW);
        var h = ActualHeight > 1 ? ActualHeight : PillH;
        MouseFollowPillLayout.StepPillFollowCursor(
            this,
            w,
            h,
            PillW,
            PillH,
            _selectionBoundsPhysical,
            MouseFollowGapBelow,
            snapToTarget: true,
            blendAlpha: null,
            blendFallback: 1,
            snapEpsilon: 0,
            avoidOverlappingSelection: false,
            clampToWorkArea: false,
            ref _pillFollowSmooth,
            out var left,
            out var top);
        Left = left;
        Top = top;
    }

    private void ResetShellEnterVisualImmediate()
    {
        ShellVisualRoot.BeginAnimation(UIElement.OpacityProperty, null);
        ShellEnterScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        ShellEnterScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        ShellVisualRoot.Opacity = 1;
        ShellEnterScale.ScaleX = 1;
        ShellEnterScale.ScaleY = 1;
        PillTextChrome.BeginAnimation(UIElement.OpacityProperty, null);
        PillTextChrome.Opacity = 1;
    }

    /// <summary>
    /// Hide when the highlight is cleared, the source element goes away, or focus moves to another tab/window
    /// without the same selection. Skip hide only while a reply is streaming.
    /// </summary>
    private void SelectionWatch_OnTick()
    {
        if (!IsVisible)
        {
            StopSelectionWatch();
            return;
        }

        if (IsCardStreaming) return;
        if (DateTime.UtcNow < _selectionWatchGraceUntil) return;

        if (!SelectionTextService.IsHighlightStillActive(
                _selectionSourceElement,
                _selectionText,
                Environment.ProcessId))
            HideOverlayAnimated(suppressReshow: false);
    }

    private void PillPanel_OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject d) return;
        for (var n = d; n != null; n = LogicalTreeHelper.GetParent(n) ?? VisualTreeHelper.GetParent(n))
        {
            if (ReferenceEquals(n, QueryBox)) return;
        }

        ScheduleFocusQueryBox();
    }

    private void QueryBox_TextChanged(object sender, TextChangedEventArgs e) => UpdatePlaceholderVisibility();

    private void QueryBox_GotFocus(object sender, RoutedEventArgs e) => UpdatePlaceholderVisibility();

    private void QueryBox_LostFocus(object sender, RoutedEventArgs e) => UpdatePlaceholderVisibility();

    private void UpdatePlaceholderVisibility()
    {
        var showPh = string.IsNullOrEmpty(QueryBox.Text);
        QueryPlaceholder.Visibility = showPh ? Visibility.Visible : Visibility.Collapsed;
        QueryBox.Foreground = showPh ? System.Windows.Media.Brushes.Transparent : ThemeManager.Brush("OverlayQueryText");
    }

    private void UpdateFollowUpPlaceholderVisibility()
    {
        var showPh = string.IsNullOrEmpty(FollowUpBox.Text);
        FollowUpPlaceholder.Visibility = showPh ? Visibility.Visible : Visibility.Collapsed;
        FollowUpBox.Foreground = showPh ? System.Windows.Media.Brushes.Transparent : ThemeManager.Brush("OverlayQueryText");
    }

    private void FollowUpBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateFollowUpPlaceholderVisibility();

    private void FollowUpBox_GotFocus(object sender, RoutedEventArgs e) => UpdateFollowUpPlaceholderVisibility();

    private void FollowUpBox_LostFocus(object sender, RoutedEventArgs e) => UpdateFollowUpPlaceholderVisibility();

    private void FollowUpBox_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        _ = SubmitFollowUpAsync();
    }

    private void ScheduleFocusFollowUpBox()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (!_hasMorphedToCard || !FollowUpBox.IsEnabled) return;
            FocusManager.SetFocusedElement(this, FollowUpBox);
            Keyboard.Focus(FollowUpBox);
            FollowUpBox.Focus();
            FollowUpBox.CaretIndex = FollowUpBox.Text.Length;
        }));
    }

    /// <summary>Reset to compact pill size and chrome (new selection, close overlay, or cancel morph).</summary>
    private void SnapToPillLayout()
    {
        _hasMorphedToCard = false;
        PillPanel.Visibility = Visibility.Visible;
        CardPanel.Visibility = Visibility.Collapsed;
        ShellOuter.CornerRadius = new CornerRadius(PillCornerRadius);
        Width = PillW;
        Height = PillH;
        MinWidth = PillMinW;
        MinHeight = PillMinH;
        MaxWidth = double.PositiveInfinity;
        MaxHeight = double.PositiveInfinity;
        ApplyPillWindowChrome();
        SyncShellChromeShape();
    }

    /// <summary>Escape or Close fully dismisses the overlay (pill and answer card).</summary>
    public void EscapeOrDismiss() => HideOverlayAnimated(suppressReshow: true);

    /// <param name="selectionBoundsScreenPhysical">Union of UIA selection rects; use empty rect if unknown.</param>
    /// <param name="cursorScreenPhysical">Used when selection bounds are missing or invalid.</param>
    /// <param name="selectionSourceElement">Focused control when the selection was made; used to detect when the highlight is cleared while the overlay keeps keyboard focus.</param>
    public void ShowForSelection(string text, string? programName, Rect selectionBoundsScreenPhysical, Point cursorScreenPhysical, AutomationElement? selectionSourceElement = null)
    {
        MouseInteractionService.ClearReshowSuppression();

        StopSelectionWatch();
        _streamCts?.Cancel();
        _streamCts = new CancellationTokenSource();
        _selectionText = text;
        _programName = programName;
        _selectionBoundsPhysical = selectionBoundsScreenPhysical;
        _cursorFallbackPhysical = cursorScreenPhysical;
        _selectionSourceElement = selectionSourceElement;
        _selectionWatchGraceUntil = DateTime.UtcNow.AddMilliseconds(700);
        _pillFollowSmooth = default;
        ResetHistoryTurn();

        SnapToPillLayout();
        QueryBox.Clear();
        ClearCardChat();
        ErrorText.Visibility = Visibility.Collapsed;
        SyncQueryPlaceholder();
        UpdatePlaceholderVisibility();

        ResetShellEnterVisualImmediate();
        Show();
        UpdateLayout();
        PositionFollowMouse();
        GlobalEscapeHotkey.TryRegister();
        StartMouseFollow();
        StartSelectionWatch();

        Activate();
        ScheduleFocusQueryBox();
        UpdateGlassChrome();
    }

    private void ApplyPillWindowChrome()
    {
        WindowChrome.SetWindowChrome(this, null);
        ResizeMode = ResizeMode.NoResize;
    }

    private void ApplyCardWindowChrome()
    {
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(8),
            GlassFrameThickness = new Thickness(0),
            UseAeroCaptionButtons = false
        });
        ResizeMode = ResizeMode.CanResize;
    }

    private void ScheduleFocusQueryBox()
    {
        void FocusOnce()
        {
            TryBringWindowToForeground();
            Activate();
            FocusManager.SetFocusedElement(this, QueryBox);
            Keyboard.Focus(QueryBox);
            QueryBox.Focus();
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(FocusOnce));
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(FocusOnce));
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(FocusOnce));
    }

    private void TryBringWindowToForeground()
    {
        try
        {
            var h = new WindowInteropHelper(this).Handle;
            if (h == IntPtr.Zero) return;

            var fg = GetForegroundWindow();
            if (fg == IntPtr.Zero)
            {
                SetForegroundWindow(h);
                return;
            }

            if (fg == h) return;

            var tidFg = GetWindowThreadProcessId(fg, out _);
            var tidCur = GetCurrentThreadId();
            if (tidFg != 0 && tidFg != tidCur)
            {
                AttachThreadInput(tidCur, tidFg, true);
                SetForegroundWindow(h);
                AttachThreadInput(tidCur, tidFg, false);
            }
            else
            {
                SetForegroundWindow(h);
            }
        }
        catch
        {
            /* ignore */
        }
    }

    private void EnsureOnScreen()
    {
        var wa = SystemParameters.WorkArea;
        if (Left + Width > wa.Right) Left = wa.Right - Width - 8;
        if (Left < wa.Left) Left = wa.Left + 8;
        if (Top + Height > wa.Bottom) Top = wa.Bottom - Height - 8;
        if (Top < wa.Top) Top = wa.Top + 8;
    }

    /// <summary>Place the window so it does not cover the source selection (prefer below, then above, then sides).</summary>
    public void PositionNearSourceSelection()
    {
        var w = ActualWidth > 1 ? ActualWidth : (_hasMorphedToCard ? CardW : PillW);
        var h = ActualHeight > 1 ? ActualHeight : (_hasMorphedToCard ? CardH : PillH);
        var wa = SystemParameters.WorkArea;
        const double gap = 10;
        const double clearance = 6;

        var hasSel = _selectionBoundsPhysical.Width >= 1 && _selectionBoundsPhysical.Height >= 1;
        var dipRect = hasSel
            ? MouseFollowPillLayout.PhysicalRectToDip(this, _selectionBoundsPhysical)
            : new Rect(MouseFollowPillLayout.PhysicalToDip(this, _cursorFallbackPhysical), new System.Windows.Size(2, 2));

        if (!hasSel)
        {
            // Cursor-only: keep previous side placement relative to point.
            const double margin = 8;
            var rightEdge = wa.Right - margin;
            var flushLeft = rightEdge - w;
            var anchorRight = dipRect.Left;
            var anchorLeft = dipRect.Left;
            double left = anchorRight + gap;
            if (left + w > rightEdge)
                left = anchorLeft - w - gap;
            if (left + w > rightEdge)
                left = flushLeft;
            if (left < wa.Left + margin)
                left = wa.Left + margin;
            var top = dipRect.Top - 2;
            MouseFollowPillLayout.ClampWindowToWorkArea(ref left, ref top, w, h);
            Left = left;
            Top = top;
            EnsureOnScreen();
            return;
        }

        var midX = dipRect.Left + dipRect.Width / 2;
        var candidates = new (double left, double top)[]
        {
            // Below / above (clear in Y) — best for reading the highlight.
            (midX - w / 2, dipRect.Bottom + gap),
            (midX - w / 2, dipRect.Top - h - gap),
            (dipRect.Left, dipRect.Bottom + gap),
            (dipRect.Right - w, dipRect.Bottom + gap),
            // Flush to monitor right under the selection when there is horizontal room.
            (Math.Min(wa.Right - 8 - w, dipRect.Right + gap), dipRect.Bottom + gap),
            // Sides (clear in X).
            (dipRect.Right + gap, dipRect.Top),
            (dipRect.Right + gap, dipRect.Top + dipRect.Height / 2 - h / 2),
            (dipRect.Right + gap, dipRect.Bottom - h),
            (dipRect.Left - w - gap, dipRect.Top),
            (dipRect.Left - w - gap, dipRect.Top + dipRect.Height / 2 - h / 2),
            (dipRect.Left - w - gap, dipRect.Bottom - h),
            // Narrow vertical gap if the monitor is tight.
            (midX - w / 2, dipRect.Bottom + 2),
            (midX - w / 2, dipRect.Top - h - 2),
        };

        foreach (var (l, t) in candidates)
        {
            var left = l;
            var top = t;
            MouseFollowPillLayout.ClampWindowToWorkArea(ref left, ref top, w, h);
            var pill = new Rect(left, top, w, h);
            if (MouseFollowPillLayout.PillOverlapsSelection(pill, dipRect, clearance))
                continue;
            Left = left;
            Top = top;
            EnsureOnScreen();
            if (!MouseFollowPillLayout.PillOverlapsSelection(new Rect(Left, Top, w, h), dipRect, clearance))
                return;
        }

        // Last resort: below selection, even if clamping / EnsureOnScreen caused slight overlap.
        {
            var left = midX - w / 2;
            var top = dipRect.Bottom + gap;
            MouseFollowPillLayout.ClampWindowToWorkArea(ref left, ref top, w, h);
            Left = left;
            Top = top;
            EnsureOnScreen();
        }
    }

    public void HideOverlayAnimated(bool suppressReshow = true)
    {
        if (!IsVisible) return;
        CloseOverlayImmediate(suppressReshow);
    }

    /// <summary>Immediate teardown (app shutdown, selection cleared, etc.).</summary>
    public void HideOverlay(bool suppressReshow = true) => CloseOverlayImmediate(suppressReshow);

    private void CloseOverlayImmediate(bool suppressReshow = true)
    {
        if (suppressReshow && !string.IsNullOrWhiteSpace(_selectionText))
            MouseInteractionService.SuppressReshowForSelection(_selectionText);

        GlobalEscapeHotkey.TryUnregister();
        BeginAnimation(WidthProperty, null);
        PillTextChrome.BeginAnimation(UIElement.OpacityProperty, null);
        ShellVisualRoot.BeginAnimation(UIElement.OpacityProperty, null);
        ShellEnterScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        ShellEnterScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

        StopSelectionWatch();
        StopMouseFollow();
        _streamCts?.Cancel();
        _streamCts = null;
        SaveHistoryIfNeeded();
        ResetHistoryTurn();
        _selectionSourceElement = null;
        _selectionText = "";
        _lastUserMessage = "";
        _lastAnswer = "";
        QueryBox.Clear();
        ClearCardChat();
        ErrorText.Visibility = Visibility.Collapsed;
        ResetShellEnterVisualImmediate();
        SnapToPillLayout();
        ClearNativeWindowRegion();
        Hide();
    }

    public Rectangle GetBoundsScreenPixels()
    {
        if (!IsVisible) return Rectangle.Empty;
        try
        {
            var topLeft = PointToScreen(new Point(0, 0));
            var br = PointToScreen(new Point(ActualWidth, ActualHeight));
            return Rectangle.FromLTRB((int)topLeft.X, (int)topLeft.Y, (int)Math.Ceiling(br.X), (int)Math.Ceiling(br.Y));
        }
        catch
        {
            return Rectangle.Empty;
        }
    }

    private void QueryBox_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        var q = QueryBox.Text.Trim();
        if (q.Length == 0) return;
        _ = SubmitAsync(q);
    }

    private async Task SubmitAsync(string question)
    {
        if (!LicenseService.IsLicensed)
        {
            ErrorText.Text = "Buy idk for this computer in Settings before asking.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        _historyRecordEnabled = true;
        _historyTitleThisTurn = question;
        _historyUtcTicks = null;

        var vision = SettingsStore.IsScreenCaptureEnabled;
        var userMsg = ChatPromptBuilder.BuildInitialUserMessage(_selectionText, question, vision);
        _lastUserMessage = userMsg;
        _overlayThreadTurns.Clear();
        ClearCardChat();
        if (!await MorphToCardAsync())
            return;

        AppendCardUserBubble(question);
        await StreamAnswerAsync(userMsg, captureVision: vision, recordHistory: true);
        if (!string.IsNullOrWhiteSpace(_lastAnswer) && ErrorText.Visibility != Visibility.Visible)
        {
            _overlayThreadTurns.Add(new ChatThreadMessageDto { Role = "user", Text = question });
            _overlayThreadTurns.Add(new ChatThreadMessageDto { Role = "assistant", Text = _lastAnswer.Trim() });
            FollowUpBox.IsEnabled = true;
            ScheduleFocusFollowUpBox();
        }
    }

    private async Task SubmitFollowUpAsync()
    {
        if (_overlayThreadTurns.Count == 0) return;
        var followUp = FollowUpBox.Text.Trim();
        if (followUp.Length == 0) return;

        var userMsg = ChatPromptBuilder.BuildFollowUpUserMessage(_selectionText, _overlayThreadTurns, followUp);
        _lastUserMessage = userMsg;
        FollowUpBox.Clear();
        UpdateFollowUpPlaceholderVisibility();

        if (!_hasMorphedToCard && !await MorphToCardAsync())
            return;

        AppendCardUserBubble(followUp);
        await StreamAnswerAsync(userMsg, captureVision: false, recordHistory: false);

        if (string.IsNullOrWhiteSpace(_lastAnswer) || ErrorText.Visibility == Visibility.Visible)
            return;

        var answer = _lastAnswer.Trim();
        _overlayThreadTurns.Add(new ChatThreadMessageDto { Role = "user", Text = followUp });
        _overlayThreadTurns.Add(new ChatThreadMessageDto { Role = "assistant", Text = answer });

        if (_historyUtcTicks is long ticks)
        {
            ChatHistoryStore.AppendThreadTurn(ticks, followUp, answer);
            RequestChatTitleSummary(ticks);
        }

        ScheduleFocusFollowUpBox();
    }

    private void ResetHistoryTurn()
    {
        _historyRecordEnabled = false;
        _historyTitleThisTurn = null;
        _historyUtcTicks = null;
        _overlayThreadTurns.Clear();
        FollowUpBox.Clear();
        UpdateFollowUpPlaceholderVisibility();
        FollowUpBox.IsEnabled = false;
    }

    private void SaveHistoryIfNeeded()
    {
        if (!_historyRecordEnabled || string.IsNullOrWhiteSpace(_historyTitleThisTurn))
            return;
        if (_lastAnswer.Trim().Length == 0)
            return;
        if (ErrorText.Visibility == Visibility.Visible)
            return;

        if (_historyUtcTicks is long ticks)
        {
            ChatHistoryStore.UpdateAnswer(ticks, _lastAnswer);
            RequestChatTitleSummary(ticks);
        }
        else if (ChatHistoryStore.AppendAndGetTicks(_selectionText, _historyTitleThisTurn, _lastAnswer) is long newTicks)
        {
            _historyUtcTicks = newTicks;
            RequestChatTitleSummary(newTicks);
        }
    }

    private static void RequestChatTitleSummary(long utcTicks) =>
        _ = ChatTitleService.RefreshTitleForEntryAsync(utcTicks);

    private Task<bool> MorphToCardAsync()
    {
        StopMouseFollow();
        PillPanel.Visibility = Visibility.Collapsed;
        CardPanel.Visibility = Visibility.Visible;

        Width = CardW;
        Height = CardH;
        ShellOuter.CornerRadius = new CornerRadius(CardCornerRadius);
        MinWidth = CardMinW;
        MinHeight = CardMinH;
        _hasMorphedToCard = true;
        ApplyCardWindowChrome();
        SyncShellChromeShape();
        UpdateLayout();
        PositionNearSourceSelection();
        UpdateGlassChrome();
        SyncShellChromeShape();
        StartSelectionWatch();
        return Task.FromResult(true);
    }

    internal void TryDismissIfHighlightCleared()
    {
        if (!IsVisible || IsCardStreaming) return;
        if (DateTime.UtcNow < _selectionWatchGraceUntil) return;
        if (!SelectionTextService.IsHighlightStillActive(
                _selectionSourceElement,
                _selectionText,
                Environment.ProcessId))
            HideOverlayAnimated(suppressReshow: false);
    }

    private async Task StreamAnswerAsync(string userMessage, bool captureVision, bool recordHistory = false)
    {
        _streamCts?.Cancel();
        _streamCts = new CancellationTokenSource();
        var ct = _streamCts.Token;

        try
        {
            if (ct.IsCancellationRequested)
                return;

            ErrorText.Visibility = Visibility.Collapsed;
            SetToolbarBusy(true);
            BeginCardAssistantStreaming();

            string? b64 = null;
            if (captureVision)
                b64 = await Task.Run(() => ScreenCaptureService.CapturePrimaryAsBase64Png(), ct);

            _lastAnswer = "";
            await foreach (var delta in OpenRouterService.StreamChatAsync(userMessage, b64, ct))
            {
                if (ct.IsCancellationRequested || !IsVisible)
                    break;
                _lastAnswer = AnswerMetaStripper.StripLeadingMeta(_lastAnswer + delta);
                UpdateCardStreamingAssistant(_lastAnswer);
            }

            StopOverlayTypingAnimation();
            if (recordHistory)
                SaveHistoryIfNeeded();
        }
        catch (OperationCanceledException)
        {
            StopOverlayTypingAnimation();
            if (recordHistory)
                SaveHistoryIfNeeded();
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
            ErrorText.Visibility = Visibility.Visible;
            StopOverlayTypingAnimation();
        }
        finally
        {
            _streamingRow = null;
            _streamingAssistantBody = null;
            SetToolbarBusy(false);
        }
    }

    private void SetToolbarBusy(bool busy)
    {
        BtnSimpler.IsEnabled = !busy;
        BtnExit.IsEnabled = true;
        FollowUpBox.IsEnabled = !busy && _overlayThreadTurns.Count > 0;
    }

    private async void OnSimpler(object sender, RoutedEventArgs e) => await RefineAsync(
        "Rewrite your previous answer in simpler language that a curious middle schooler could follow. Keep the same meaning. Output only the revised answer.");

    private async Task RefineAsync(string instruction)
    {
        if (_lastUserMessage.Length == 0 || _lastAnswer.Length == 0) return;
        var combined = $"{_lastUserMessage}\n\n[Assistant]\n{_lastAnswer}\n\n[User]\n{instruction}";
        RemoveLastCardAssistantBubble();
        await StreamAnswerAsync(combined, captureVision: false, recordHistory: true);
        if (string.IsNullOrWhiteSpace(_lastAnswer) || ErrorText.Visibility == Visibility.Visible)
            return;

        var answer = _lastAnswer.Trim();
        for (var i = _overlayThreadTurns.Count - 1; i >= 0; i--)
        {
            if (_overlayThreadTurns[i].IsAssistant)
            {
                _overlayThreadTurns[i].Text = answer;
                break;
            }
        }

        if (_historyUtcTicks is long ticks)
            ChatHistoryStore.UpdateAnswer(ticks, answer);
    }

    private void OnExitClick(object sender, RoutedEventArgs e) => EscapeOrDismiss();

    private void ClearCardChat()
    {
        StopOverlayTypingAnimation();
        _streamingRow = null;
        _streamingAssistantBody = null;
        CardMessagesPanel.Children.Clear();
    }

    private void AppendCardUserBubble(string text) =>
        CardMessagesPanel.Children.Add(OverlayChatUi.CreateUserBubble(text));

    private void BeginCardAssistantStreaming()
    {
        var (row, body) = OverlayChatUi.CreateAssistantBubble("", streaming: true);
        _streamingRow = row;
        _streamingAssistantBody = body;
        CardMessagesPanel.Children.Add(row);
        StartOverlayTypingAnimation();
    }

    private void PromoteCardStreamingToText(string streamText)
    {
        if (_streamingRow == null) return;
        StopOverlayTypingAnimation();
        var idx = CardMessagesPanel.Children.IndexOf(_streamingRow);
        if (idx < 0) return;

        var (row, body) = OverlayChatUi.CreateAssistantBubble(streamText);
        CardMessagesPanel.Children.RemoveAt(idx);
        CardMessagesPanel.Children.Insert(idx, row);
        _streamingRow = row;
        _streamingAssistantBody = body;
    }

    private void UpdateCardStreamingAssistant(string streamText)
    {
        if (string.IsNullOrWhiteSpace(streamText)) return;

        if (_streamingAssistantBody == null && _streamingRow != null)
            PromoteCardStreamingToText(streamText);
        else if (_streamingAssistantBody != null)
            MarkdownAnswerFormatter.ApplyDisplayMarkdown(_streamingAssistantBody, streamText);
    }

    private void RemoveLastCardAssistantBubble()
    {
        for (var i = CardMessagesPanel.Children.Count - 1; i >= 0; i--)
        {
            if (CardMessagesPanel.Children[i] is FrameworkElement { Tag: OverlayChatUi.TagAssistant })
            {
                CardMessagesPanel.Children.RemoveAt(i);
                _streamingRow = null;
                _streamingAssistantBody = null;
                return;
            }
        }
    }

    private void StartOverlayTypingAnimation()
    {
        StopOverlayTypingAnimation();
        if (_streamingRow is not Grid grid) return;
        var dotsPanel = FindTypingDotsPanel(grid);
        if (dotsPanel == null) return;

        _overlayTypingDots = dotsPanel.Children.OfType<System.Windows.Shapes.Ellipse>().ToArray();
        if (_overlayTypingDots.Length == 0) return;

        _overlayTypingFrame = 0;
        _overlayTypingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(340) };
        _overlayTypingTimer.Tick += (_, _) =>
        {
            if (_overlayTypingDots == null) return;
            var active = _overlayTypingFrame % _overlayTypingDots.Length;
            for (var i = 0; i < _overlayTypingDots.Length; i++)
                _overlayTypingDots[i].Opacity = i == active ? 1.0 : 0.35;
            _overlayTypingFrame++;
        };
        _overlayTypingTimer.Start();
    }

    private void StopOverlayTypingAnimation()
    {
        _overlayTypingTimer?.Stop();
        _overlayTypingTimer = null;
        _overlayTypingDots = null;
    }

    private static StackPanel? FindTypingDotsPanel(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is StackPanel sp && sp.Children.Count >= 3 && sp.Children[0] is System.Windows.Shapes.Ellipse)
                return sp;
            var found = FindTypingDotsPanel(child);
            if (found != null) return found;
        }

        return null;
    }

    private void CardHeader_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_hasMorphedToCard || e.LeftButton != MouseButtonState.Pressed) return;
        if (e.OriginalSource is not DependencyObject d) return;
        for (var n = d; n != null; n = VisualTreeHelper.GetParent(n))
        {
            if (n is System.Windows.Controls.Button or System.Windows.Controls.TextBox) return;
        }

        try
        {
            DragMove();
        }
        catch
        {
            /* ignore */
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
