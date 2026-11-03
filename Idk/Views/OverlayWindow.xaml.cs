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
    private const double PillH = 32;
    private const double PillMinW = 128;
    private const double PillMinH = 32;
    private const double PillCornerRadius = 16;
    private const double CardCornerRadius = 18;
    private const double CardW = 320;
    private const double CardH = 340;
    private const double CardMinW = 220;
    private const double CardMinH = 168;

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
    private const double BottomCenterMargin = 18;
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
        SizeChanged += (_, _) =>
        {
            SyncShellChromeShape();
            if (IsVisible && SettingsStore.AskBarPlacement == AskBarPlacementMode.BottomCenter)
                PositionBottomCenter();
        };
        DpiChanged += (_, _) => SyncShellChromeShape();
        ThemeManager.ThemeChanged += OnAppThemeChanged;
    }

    private void OnAppThemeChanged() => ApplyTheme();

    private void OnOverlaySourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        if (HwndSource.FromHwnd(hwnd) is HwndSource source)
            source.AddHook(OverlayHitTestHook);
        WindowsRoundedWindowCorners.TryDoNotRound(this, suppressDwmBorder: true);
        ClearNativeWindowRegion();
        ScreenShareVisibility.Apply(this);
        GlobalEscapeHotkey.Attach(this);
    }

    private void OnKeyboardFocusChanged(object sender, KeyboardFocusChangedEventArgs e) => UpdateGlassChrome();

    private void AnswerScroll_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e) =>
        SmoothScroll.HandlePreviewMouseWheel(sender as ScrollViewer, e, 0.2);

    public void RefreshChromeSettings()
    {
        ApplyTheme();
        SyncQueryPlaceholder();
        ApplyAskBarPlacement();
        ScreenShareVisibility.Apply(this);
    }

    public void ApplyTheme()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(ApplyTheme);
            return;
        }

        Background = System.Windows.Media.Brushes.Transparent;
        RootContentGrid.Background = System.Windows.Media.Brushes.Transparent;
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

        CardFollowUpBar.Background = ThemeManager.Brush("OverlayAssistantBg");
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
    /// The capsule is one antialiased border. Extra clips and a GDI window region cut that curve into stairs.
    /// </summary>
    private void SyncShellChromeShape(bool syncNativeRegion = true)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => SyncShellChromeShape(syncNativeRegion));
            return;
        }

        ShellCornerFill.CornerRadius = ShellOuter.CornerRadius;
        ShellOuter.Clip = null;
        ShellVisualRoot.Clip = null;
        RootContentGrid.Clip = null;
        ApplyContentClip();
        if (syncNativeRegion)
            ApplyNativeWindowShape();
    }

    /// <summary>
    /// Child backgrounds are rectangles. Clip them to the shell curve so the bottom corners stay round.
    /// The shell border itself is not clipped, so the edge stays smooth.
    /// </summary>
    private void ApplyContentClip()
    {
        var w = ShellOuter.ActualWidth;
        var h = ShellOuter.ActualHeight;
        if (w < 2 || h < 2)
        {
            ShellContent.Clip = null;
            return;
        }

        var radius = Math.Max(0, ShellOuter.CornerRadius.TopLeft);
        ShellContent.Clip = new RectangleGeometry(new Rect(0, 0, w, h), radius, radius);
    }

    private const int WmNcHitTest = 0x0084;
    private const int HtTransparent = -1;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    /// <summary>
    /// Corners outside the capsule stay transparent and click through. The curve itself is drawn by WPF.
    /// </summary>
    private IntPtr OverlayHitTestHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmNcHitTest || !IsVisible) return IntPtr.Zero;
        try
        {
            var lp = lParam.ToInt32();
            var screen = new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF));
            if (HitTestShell(PointFromScreen(screen))) return IntPtr.Zero;
            handled = true;
            return (IntPtr)HtTransparent;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    private bool HitTestShell(Point local)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w < 1 || h < 1) return false;
        var r = Math.Min(ShellOuter.CornerRadius.TopLeft, Math.Min(w, h) / 2);
        const double pad = 1.25;
        if (local.X < -pad || local.Y < -pad || local.X > w + pad || local.Y > h + pad)
            return false;
        var cx = Math.Clamp(local.X, r, Math.Max(r, w - r));
        var cy = Math.Clamp(local.Y, r, Math.Max(r, h - r));
        var dx = local.X - cx;
        var dy = local.Y - cy;
        var reach = r + pad;
        return dx * dx + dy * dy <= reach * reach;
    }

    private void ClearNativeWindowRegion()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
            SetWindowRgn(hwnd, IntPtr.Zero, true);
    }

    /// <summary>
    /// Follow-the-cursor keeps a transparent window so the curve can antialias.
    /// Parked at the bottom, that same window paints a white rectangle in the corners.
    /// A capsule region clips the plate off so the ends stay round.
    /// </summary>
    private void ApplyNativeWindowShape()
    {
        if (SettingsStore.AskBarPlacement != AskBarPlacementMode.BottomCenter)
        {
            ClearNativeWindowRegion();
            return;
        }

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        WindowsRoundedWindowCorners.TryDoNotRound(this, suppressDwmBorder: true);

        var (sx, sy) = DeviceScale();
        var w = ActualWidth > 1 ? ActualWidth : Width;
        var h = ActualHeight > 1 ? ActualHeight : Height;
        if (w < 1 || h < 1) return;

        var pw = Math.Max(1, (int)Math.Round(w * sx));
        var ph = Math.Max(1, (int)Math.Round(h * sy));
        var radiusDip = Math.Min(ShellOuter.CornerRadius.TopLeft, Math.Min(w, h) / 2);
        var dia = Math.Max(1, (int)Math.Round(radiusDip * 2 * Math.Min(sx, sy)));
        dia = Math.Min(dia, Math.Min(pw, ph));
        var rgn = CreateRoundRectRgn(0, 0, pw + 1, ph + 1, dia, dia);
        if (rgn == IntPtr.Zero) return;
        if (SetWindowRgn(hwnd, rgn, true) == 0)
            DeleteObject(rgn);
    }

    private (double X, double Y) DeviceScale()
    {
        var m = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        var sx = Math.Abs(m.M11) < 0.01 ? 1 : m.M11;
        var sy = Math.Abs(m.M22) < 0.01 ? 1 : m.M22;
        return (sx, sy);
    }

    /// <summary>Land the layered window on physical pixels so the capsule is not resampled.</summary>
    private void MoveOverlayTo(double left, double top)
    {
        var (sx, sy) = DeviceScale();
        Left = Math.Round(left * sx) / sx;
        Top = Math.Round(top * sy) / sy;
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

        if (SettingsStore.AskBarPlacement == AskBarPlacementMode.BottomCenter)
        {
            StopMouseFollow();
            PositionBottomCenter();
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
        MoveOverlayTo(left, top);
    }

    /// <summary>Mini ask bar: follow the pointer, or stay locked at the bottom center of that screen.</summary>
    private void ApplyAskBarPlacement()
    {
        if (!IsVisible) return;

        if (SettingsStore.AskBarPlacement == AskBarPlacementMode.BottomCenter)
        {
            StopMouseFollow();
            PositionBottomCenter();
            ApplyNativeWindowShape();
            return;
        }

        if (_hasMorphedToCard)
        {
            ApplyNativeWindowShape();
            return;
        }

        ApplyNativeWindowShape();

        PositionFollowMouse();
        if (_followRenderingHandler == null)
            StartMouseFollow();
    }

    private void PositionBottomCenter()
    {
        if (!IsVisible) return;

        var w = ActualWidth > 1 ? ActualWidth : (_hasMorphedToCard ? CardW : PillW);
        var h = ActualHeight > 1 ? ActualHeight : (_hasMorphedToCard ? CardH : PillH);
        void Place()
        {
            MouseFollowPillLayout.PlaceAtBottomCenter(
                this,
                _cursorFallbackPhysical,
                w,
                h,
                BottomCenterMargin,
                out var left,
                out var top);
            MoveOverlayTo(left, top);
        }

        Place();
        UpdateLayout();
        Place();
    }

    private void ResetShellEnterVisualImmediate()
    {
        ShellVisualRoot.BeginAnimation(UIElement.OpacityProperty, null);
        ShellVisualRoot.Opacity = 1;
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
        SyncSelectionPreview();
        ErrorText.Visibility = Visibility.Collapsed;
        SyncQueryPlaceholder();
        UpdatePlaceholderVisibility();

        ResetShellEnterVisualImmediate();
        Show();
        UpdateLayout();
        GlobalEscapeHotkey.TryRegister();
        ApplyAskBarPlacement();
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
            ResizeBorderThickness = new Thickness(0),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(CardCornerRadius),
            UseAeroCaptionButtons = false
        });
        ResizeMode = ResizeMode.NoResize;
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
        var left = Left;
        var top = Top;
        if (left + Width > wa.Right) left = wa.Right - Width - 8;
        if (left < wa.Left) left = wa.Left + 8;
        if (top + Height > wa.Bottom) top = wa.Bottom - Height - 8;
        if (top < wa.Top) top = wa.Top + 8;
        MoveOverlayTo(left, top);
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
            MoveOverlayTo(left, top);
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
            MoveOverlayTo(left, top);
            EnsureOnScreen();
            if (!MouseFollowPillLayout.PillOverlapsSelection(new Rect(Left, Top, w, h), dipRect, clearance))
                return;
        }

        // Last resort: below selection, even if clamping / EnsureOnScreen caused slight overlap.
        {
            var left = midX - w / 2;
            var top = dipRect.Bottom + gap;
            MouseFollowPillLayout.ClampWindowToWorkArea(ref left, ref top, w, h);
            MoveOverlayTo(left, top);
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

        StopSelectionWatch();
        StopMouseFollow();
        _streamCts?.Cancel();
        _streamCts = null;
        SaveHistoryIfNeeded();
        ResetHistoryTurn();
        _selectionSourceElement = null;
        _selectionText = "";
        SyncSelectionPreview();
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

    private async Task<bool> EnsureCanAskAsync()
    {
        if (FreeAskQuota.CanAsk)
        {
            UpgradeButton.Visibility = Visibility.Collapsed;
            return true;
        }

        if (!_hasMorphedToCard && !await MorphToCardAsync())
            return false;

        ErrorText.Text = FreeAskQuota.BlockedMessage;
        ErrorText.Visibility = Visibility.Visible;
        UpgradeButton.Visibility = Visibility.Visible;
        return false;
    }

    private void Upgrade_Click(object sender, RoutedEventArgs e)
    {
        HideOverlay(suppressReshow: true);
        AppShellWindow.OpenBilling();
    }

    private async Task SubmitAsync(string question)
    {
        if (!await EnsureCanAskAsync())
            return;

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
            FreeAskQuota.RecordSuccess();
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
        if (!await EnsureCanAskAsync())
            return;

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

        FreeAskQuota.RecordSuccess();
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
        if (SettingsStore.AskBarPlacement == AskBarPlacementMode.BottomCenter)
            PositionBottomCenter();
        else
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
            {
                ScreenShareVisibility.ForceExcludeAll();
                try
                {
                    b64 = await Task.Run(() => ScreenCaptureService.CapturePrimaryAsBase64Png(), ct);
                }
                finally
                {
                    ScreenShareVisibility.ApplyAll();
                }
            }

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
            UpgradeButton.Visibility = ex.Message == FreeAskQuota.BlockedMessage
                ? Visibility.Visible
                : Visibility.Collapsed;
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
        if (!await EnsureCanAskAsync())
            return;
        var combined = $"{_lastUserMessage}\n\n[Assistant]\n{_lastAnswer}\n\n[User]\n{instruction}";
        RemoveLastCardAssistantBubble();
        await StreamAnswerAsync(combined, captureVision: false, recordHistory: true);
        if (string.IsNullOrWhiteSpace(_lastAnswer) || ErrorText.Visibility == Visibility.Visible)
            return;

        FreeAskQuota.RecordSuccess();
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

    private void SyncSelectionPreview()
    {
        var preview = SelectionSnippetFormatter.ToPreview(_selectionText, 120);
        SelectionPreview.Text = preview;
        SelectionPreview.ToolTip = string.IsNullOrEmpty(preview)
            ? null
            : SelectionSnippetFormatter.ToPreview(_selectionText, 480);
        SelectionPreview.Visibility = string.IsNullOrEmpty(preview) ? Visibility.Collapsed : Visibility.Visible;
    }

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
        if (SettingsStore.AskBarPlacement == AskBarPlacementMode.BottomCenter) return;
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
