using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Idk.Services;

/// <summary>Gentler, eased vertical scrolling for dashboard chat panes.</summary>
public static class SmoothScrollHelper
{
    private const double PixelsPerNotch = 72;
    private const double DurationMs = 160;

    private static readonly ConditionalWeakTable<ScrollViewer, ScrollState> States = new();

    public static void OnPreviewMouseWheel(ScrollViewer viewer, MouseWheelEventArgs e)
    {
        if (viewer.ScrollableHeight <= 0.5) return;

        e.Handled = true;
        var state = States.GetValue(viewer, static _ => new ScrollState());
        StopAnimation(viewer, state);

        var step = e.Delta > 0 ? -PixelsPerNotch : PixelsPerNotch;
        state.From = viewer.VerticalOffset;
        state.To = Math.Clamp(state.From + step, 0, viewer.ScrollableHeight);
        state.Sw = Stopwatch.StartNew();
        state.RenderingHandler = (_, _) => OnRendering(viewer, state);
        CompositionTarget.Rendering += state.RenderingHandler;
    }

    private static void OnRendering(ScrollViewer viewer, ScrollState state)
    {
        var u = Math.Min(1.0, state.Sw!.Elapsed.TotalMilliseconds / DurationMs);
        var eased = 1 - Math.Pow(1 - u, 3);
        viewer.ScrollToVerticalOffset(state.From + (state.To - state.From) * eased);
        if (u >= 1)
            StopAnimation(viewer, state);
    }

    private static void StopAnimation(ScrollViewer viewer, ScrollState state)
    {
        if (state.RenderingHandler == null) return;
        CompositionTarget.Rendering -= state.RenderingHandler;
        state.RenderingHandler = null;
        state.Sw = null;
    }

    private sealed class ScrollState
    {
        public EventHandler? RenderingHandler;
        public Stopwatch? Sw;
        public double From;
        public double To;
    }
}
