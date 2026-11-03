using System.Windows.Automation;

namespace Idk.Services;

public static class SelectionTextService
{
    public static string? TryGetSelectedText()
    {
        try
        {
            return TryGetSelectedTextFromElement(AutomationElement.FocusedElement);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the highlight from the focused control, its parents, and the control under the cursor.
    /// Editors and browsers often keep the selection on an ancestor, not the focused node.
    /// </summary>
    public static string? TryGetSelectedTextNear(System.Windows.Point screenPoint)
    {
        var fromFocus = TryFromElementAndAncestors(TryFocused());
        if (IsUsableSelection(fromFocus)) return fromFocus;

        AutomationElement? underCursor = null;
        try
        {
            underCursor = AutomationElement.FromPoint(screenPoint);
        }
        catch
        {
            underCursor = null;
        }

        var fromCursor = TryFromElementAndAncestors(underCursor);
        return IsUsableSelection(fromCursor) ? fromCursor : null;
    }

    private static AutomationElement? TryFocused()
    {
        try
        {
            return AutomationElement.FocusedElement;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryFromElementAndAncestors(AutomationElement? el)
    {
        for (var i = 0; i < 14 && el != null; i++)
        {
            var text = TryGetSelectedTextFromElement(el)?.Trim();
            if (IsUsableSelection(text)) return text;

            try
            {
                el = TreeWalker.ControlViewWalker.GetParent(el);
            }
            catch
            {
                break;
            }
        }

        return null;
    }

    private static bool IsUsableSelection(string? text) =>
        !string.IsNullOrWhiteSpace(text) && text.Trim().Length >= 2;

    /// <summary>Reads the current text selection from a specific element (e.g. the host editor), even when it is not the UIA focused element.</summary>
    public static string? TryGetSelectedTextFromElement(AutomationElement? el)
    {
        if (el == null) return null;
        try
        {
            if (!el.TryGetCurrentPattern(TextPattern.Pattern, out var pattern)) return null;
            if (pattern is not TextPattern tp) return null;
            var ranges = tp.GetSelection();
            if (ranges.Length == 0) return string.Empty;
            return ranges[0].GetText(-1);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Screen-space union of visible selection rectangles (physical pixels), if UIA exposes them.</summary>
    public static bool TryGetSelectionScreenBounds(out System.Windows.Rect bounds) =>
        TryGetSelectionScreenBoundsFromElement(AutomationElement.FocusedElement, out bounds);

    /// <summary>Screen-space union of selection rectangles for a specific element.</summary>
    public static bool TryGetSelectionScreenBoundsFromElement(AutomationElement? el, out System.Windows.Rect bounds)
    {
        bounds = default;
        if (el == null) return false;
        try
        {
            if (!el.TryGetCurrentPattern(TextPattern.Pattern, out var pattern)) return false;
            if (pattern is not TextPattern tp) return false;
            var ranges = tp.GetSelection();
            if (ranges.Length == 0) return false;
            var rects = ranges[0].GetBoundingRectangles();
            if (rects == null || rects.Length == 0) return false;
            return TryUnionBoundingRectangles(rects, out bounds);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Whether the highlight that opened the overlay is still present.
    /// Always checks the captured source element; uses selection bounds when text reads fail.
    /// When focus moves within the same window (e.g. another tab), the focused control must still show the same highlight.
    /// </summary>
    public static bool IsHighlightStillActive(AutomationElement? sourceElement, string expectedText, int idkProcessId)
    {
        var expected = expectedText.Trim();
        if (expected.Length < 2) return false;

        // The ask bar takes focus. Many apps then drop the highlight, which used to dismiss the bar immediately.
        if (IsIdkProcessFocused(idkProcessId))
            return true;

        if (sourceElement == null || !IsElementAvailable(sourceElement))
            return IsFocusedHighlightMatching(expected, idkProcessId);

        if (!SourceStillHasExpectedHighlight(sourceElement, expected))
            return false;

        if (HasConflictingFocusInSameWindow(sourceElement, expected, idkProcessId))
            return false;

        return true;
    }

    /// <summary>True when the captured source still shows the expected highlight (text and/or non-empty bounds).</summary>
    public static bool SourceStillHasExpectedHighlight(AutomationElement? sourceElement, string expectedText)
    {
        var expected = expectedText.Trim();
        if (expected.Length < 2 || sourceElement == null || !IsElementAvailable(sourceElement))
            return false;

        var sourceText = TryGetSelectedTextFromElement(sourceElement)?.Trim();
        if (!string.IsNullOrEmpty(sourceText) && sourceText.Length >= 2)
            return string.Equals(sourceText, expected, StringComparison.Ordinal);

        return TryGetSelectionScreenBoundsFromElement(sourceElement, out var bounds)
               && bounds.Width >= 1
               && bounds.Height >= 1;
    }

    private static bool IsFocusedHighlightMatching(string expected, int idkProcessId)
    {
        if (IsIdkProcessFocused(idkProcessId)) return true;

        var focusedText = TryGetSelectedText()?.Trim();
        return !string.IsNullOrEmpty(focusedText)
               && focusedText.Length >= 2
               && string.Equals(focusedText, expected, StringComparison.Ordinal);
    }

    private static bool HasConflictingFocusInSameWindow(
        AutomationElement sourceElement,
        string expected,
        int idkProcessId)
    {
        AutomationElement? focused;
        try
        {
            focused = AutomationElement.FocusedElement;
        }
        catch
        {
            return false;
        }

        if (focused == null) return false;

        try
        {
            if (focused.Current.ProcessId == idkProcessId)
                return false;
        }
        catch
        {
            return false;
        }

        if (IsSameOrRelated(sourceElement, focused))
            return false;

        var srcTop = TryGetTopLevelNativeHandle(sourceElement);
        var fgTop = TryGetTopLevelNativeHandle(focused);
        if (srcTop == IntPtr.Zero || fgTop == IntPtr.Zero || srcTop != fgTop)
            return false;

        var focusedText = TryGetSelectedTextFromElement(focused)?.Trim();
        return string.IsNullOrEmpty(focusedText)
               || focusedText.Length < 2
               || !string.Equals(focusedText, expected, StringComparison.Ordinal);
    }

    public static bool IsIdkProcessFocused(int idkProcessId)
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            return focused != null && focused.Current.ProcessId == idkProcessId;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsElementAvailable(AutomationElement? el)
    {
        if (el == null) return false;
        try
        {
            _ = el.Current.ProcessId;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryUnionBoundingRectangles(System.Windows.Rect[] rects, out System.Windows.Rect bounds)
    {
        bounds = default;
        double minL = double.PositiveInfinity, minT = double.PositiveInfinity;
        double maxR = double.NegativeInfinity, maxB = double.NegativeInfinity;

        foreach (var r in rects)
        {
            if (r.Width < 0.5 || r.Height < 0.5) continue;
            minL = Math.Min(minL, r.Left);
            minT = Math.Min(minT, r.Top);
            maxR = Math.Max(maxR, r.Right);
            maxB = Math.Max(maxB, r.Bottom);
        }

        if (!double.IsFinite(minL) || maxR <= minL + 0.5 || maxB <= minT + 0.5) return false;
        bounds = new System.Windows.Rect(minL, minT, maxR - minL, maxB - minT);
        return true;
    }

    private static bool IsSameOrRelated(AutomationElement a, AutomationElement b)
    {
        if (a.Equals(b)) return true;
        try
        {
            var walker = TreeWalker.ControlViewWalker;
            for (var n = walker.GetParent(b); n != null; n = walker.GetParent(n))
            {
                if (n.Equals(a)) return true;
            }

            for (var n = walker.GetParent(a); n != null; n = walker.GetParent(n))
            {
                if (n.Equals(b)) return true;
            }
        }
        catch
        {
            /* ignore */
        }

        return false;
    }

    private static IntPtr TryGetTopLevelNativeHandle(AutomationElement el)
    {
        try
        {
            var walker = TreeWalker.ControlViewWalker;
            var node = el;
            IntPtr last = IntPtr.Zero;
            while (node != null)
            {
                var hwnd = new IntPtr(node.Current.NativeWindowHandle);
                if (hwnd != IntPtr.Zero)
                    last = hwnd;
                node = walker.GetParent(node);
            }

            return last;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }
}
