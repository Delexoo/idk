using System.Windows.Controls;
using System.Windows.Input;

namespace Idk.Services;

/// <summary>Reduces mouse-wheel scroll distance for steadier scrolling in ScrollViewers.</summary>
public static class SmoothScroll
{
    /// <param name="pixelsPerDeltaUnit">Scale applied to <see cref="MouseWheelEventArgs.Delta"/> (typical delta is ±120 per notch).</param>
    public static void HandlePreviewMouseWheel(ScrollViewer? viewer, MouseWheelEventArgs e, double pixelsPerDeltaUnit = 0.22)
    {
        if (viewer == null) return;
        if (!double.IsFinite(viewer.ScrollableHeight) || viewer.ScrollableHeight <= 0) return;

        var next = viewer.VerticalOffset - e.Delta * pixelsPerDeltaUnit;
        if (next < 0) next = 0;
        else if (next > viewer.ScrollableHeight) next = viewer.ScrollableHeight;

        viewer.ScrollToVerticalOffset(next);
        e.Handled = true;
    }
}
