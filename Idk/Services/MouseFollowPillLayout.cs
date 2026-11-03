using System.Windows;
using System.Windows.Media;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;

namespace Idk.Services;

/// <summary>Shared DIP/physical conversion, work-area clamp, and pill-under-cursor placement.</summary>
public static class MouseFollowPillLayout
{
    public struct PillFollowSmoothState
    {
        public double SmoothLeft;
        public double SmoothTop;
        public bool Initialized;
    }

    public static Point PhysicalToDip(Visual dpiVisual, Point physical)
    {
        var src = PresentationSource.FromVisual(dpiVisual);
        if (src?.CompositionTarget == null) return physical;
        return src.CompositionTarget.TransformFromDevice.Transform(physical);
    }

    public static Rect PhysicalRectToDip(Visual dpiVisual, Rect physical)
    {
        var src = PresentationSource.FromVisual(dpiVisual);
        if (src?.CompositionTarget == null) return physical;
        var t = src.CompositionTarget.TransformFromDevice;
        var a = t.Transform(new Point(physical.Left, physical.Top));
        var b = t.Transform(new Point(physical.Right, physical.Bottom));
        return new Rect(a, b);
    }

    public static void ClampWindowToWorkArea(ref double left, ref double top, double w, double h)
    {
        var wa = SystemParameters.WorkArea;
        const double m = 8;
        if (left + w > wa.Right - m) left = wa.Right - w - m;
        if (left < wa.Left + m) left = wa.Left + m;
        if (top + h > wa.Bottom - m) top = wa.Bottom - h - m;
        if (top < wa.Top + m) top = wa.Top + m;
    }

    /// <summary>True if the pill (axis-aligned) overlaps the selection bounds expanded by clearance.</summary>
    public static bool PillOverlapsSelection(Rect pill, Rect selectionDip, double clearance)
    {
        if (selectionDip.Width < 1 || selectionDip.Height < 1) return false;
        var s = new Rect(
            selectionDip.Left - clearance,
            selectionDip.Top - clearance,
            selectionDip.Width + 2 * clearance,
            selectionDip.Height + 2 * clearance);
        return !(pill.Left >= s.Right || pill.Right <= s.Left || pill.Top >= s.Bottom || pill.Bottom <= s.Top);
    }

    /// <summary>Pill mode: centered on the cursor horizontally, just below it; optionally nudged off the source selection.</summary>
    public static void StepPillFollowCursor(
        Visual dpiVisual,
        double actualWidth,
        double actualHeight,
        double defaultWidth,
        double defaultHeight,
        in Rect selectionBoundsPhysical,
        double mouseFollowGapBelow,
        bool snapToTarget,
        double? blendAlpha,
        double blendFallback,
        double snapEpsilon,
        bool avoidOverlappingSelection,
        bool clampToWorkArea,
        ref PillFollowSmoothState smooth,
        out double left,
        out double top)
    {
        var w = actualWidth > 1 ? actualWidth : defaultWidth;
        var h = actualHeight > 1 ? actualHeight : defaultHeight;
        var curPhys = new Point(System.Windows.Forms.Cursor.Position.X, System.Windows.Forms.Cursor.Position.Y);
        var curDip = PhysicalToDip(dpiVisual, curPhys);

        double targetLeft = curDip.X - w / 2;
        double targetTop = curDip.Y + mouseFollowGapBelow;
        if (clampToWorkArea)
            ClampWindowToWorkArea(ref targetLeft, ref targetTop, w, h);

        if (avoidOverlappingSelection)
        {
            var hasSel = selectionBoundsPhysical.Width >= 1 && selectionBoundsPhysical.Height >= 1;
            if (hasSel)
            {
                var dipRect = PhysicalRectToDip(dpiVisual, selectionBoundsPhysical);
                const double clearance = 6;
                const double gap = 10;
                if (PillOverlapsSelection(new Rect(targetLeft, targetTop, w, h), dipRect, clearance))
                {
                    var altLeft = dipRect.Right + gap;
                    var altTop = curDip.Y + mouseFollowGapBelow;
                    if (clampToWorkArea)
                        ClampWindowToWorkArea(ref altLeft, ref altTop, w, h);
                    if (!PillOverlapsSelection(new Rect(altLeft, altTop, w, h), dipRect, clearance))
                    {
                        targetLeft = altLeft;
                        targetTop = altTop;
                    }
                    else
                    {
                        altLeft = curDip.X - w / 2;
                        altTop = dipRect.Bottom + gap;
                        if (clampToWorkArea)
                            ClampWindowToWorkArea(ref altLeft, ref altTop, w, h);
                        if (!PillOverlapsSelection(new Rect(altLeft, altTop, w, h), dipRect, clearance))
                        {
                            targetLeft = altLeft;
                            targetTop = altTop;
                        }
                        else
                        {
                            altLeft = curDip.X - w / 2;
                            altTop = dipRect.Top - h - gap;
                            if (clampToWorkArea)
                                ClampWindowToWorkArea(ref altLeft, ref altTop, w, h);
                            if (!PillOverlapsSelection(new Rect(altLeft, altTop, w, h), dipRect, clearance))
                            {
                                targetLeft = altLeft;
                                targetTop = altTop;
                            }
                        }
                    }
                }
            }
        }

        var k = blendAlpha ?? blendFallback;

        if (snapToTarget)
        {
            smooth.SmoothLeft = targetLeft;
            smooth.SmoothTop = targetTop;
            smooth.Initialized = true;
        }
        else if (!smooth.Initialized)
        {
            smooth.SmoothLeft = targetLeft;
            smooth.SmoothTop = targetTop;
            smooth.Initialized = true;
        }
        else
        {
            var dl = targetLeft - smooth.SmoothLeft;
            var dt = targetTop - smooth.SmoothTop;
            if (Math.Abs(dl) < snapEpsilon && Math.Abs(dt) < snapEpsilon)
            {
                smooth.SmoothLeft = targetLeft;
                smooth.SmoothTop = targetTop;
            }
            else
            {
                smooth.SmoothLeft += dl * k;
                smooth.SmoothTop += dt * k;
            }
        }

        var sl = smooth.SmoothLeft;
        var st = smooth.SmoothTop;
        if (clampToWorkArea)
            ClampWindowToWorkArea(ref sl, ref st, w, h);
        smooth.SmoothLeft = sl;
        smooth.SmoothTop = st;
        left = sl;
        top = st;
    }

    /// <summary>Lock the pill to the horizontal center of the work area, just above the bottom edge.</summary>
    public static void PlaceAtBottomCenter(
        Visual dpiVisual,
        Point anchorPhysical,
        double width,
        double height,
        double bottomMargin,
        out double left,
        out double top)
    {
        var w = Math.Max(1, width);
        var h = Math.Max(1, height);
        var src = PresentationSource.FromVisual(dpiVisual);
        if (src?.CompositionTarget == null)
        {
            var wa = SystemParameters.WorkArea;
            left = wa.Left + (wa.Width - w) / 2;
            top = wa.Bottom - h - bottomMargin;
            return;
        }

        var screen = System.Windows.Forms.Screen.FromPoint(
            new System.Drawing.Point(
                (int)Math.Round(anchorPhysical.X),
                (int)Math.Round(anchorPhysical.Y)));
        var area = screen.WorkingArea;
        var dip = PhysicalRectToDip(dpiVisual, new Rect(area.Left, area.Top, area.Width, area.Height));
        left = dip.Left + (dip.Width - w) / 2;
        top = dip.Bottom - h - bottomMargin;

        const double edge = 8;
        if (left < dip.Left + edge) left = dip.Left + edge;
        if (left + w > dip.Right - edge) left = Math.Max(dip.Left + edge, dip.Right - edge - w);
        if (top + h > dip.Bottom - edge) top = dip.Bottom - h - edge;
        if (top < dip.Top + edge) top = dip.Top + edge;
    }

    public static void ClampToParent(ref double left, ref double top, double w, double h, double parentW, double parentH, double margin = 8)
    {
        if (left + w > parentW - margin) left = parentW - w - margin;
        if (left < margin) left = margin;
        if (top + h > parentH - margin) top = parentH - h - margin;
        if (top < margin) top = margin;
    }

    /// <summary>Pill follow inside a parent (e.g. Canvas): cursor-centered below pointer, optionally nudged off the selection rect.</summary>
    public static void StepPillFollowInParent(
        System.Windows.Size parentSize,
        in Rect selectionLocal,
        Point cursorLocal,
        double actualPillW,
        double actualPillH,
        double defaultPillW,
        double defaultPillH,
        double mouseFollowGapBelow,
        bool snapToTarget,
        double? blendAlpha,
        double blendFallback,
        double snapEpsilon,
        bool avoidOverlappingSelection,
        ref PillFollowSmoothState smooth,
        out double left,
        out double top)
    {
        var pw = Math.Max(64, parentSize.Width);
        var ph = Math.Max(64, parentSize.Height);
        var w = actualPillW > 1 ? actualPillW : defaultPillW;
        var h = actualPillH > 1 ? actualPillH : defaultPillH;

        double targetLeft = cursorLocal.X - w / 2;
        double targetTop = cursorLocal.Y + mouseFollowGapBelow;
        ClampToParent(ref targetLeft, ref targetTop, w, h, pw, ph);

        if (avoidOverlappingSelection)
        {
            var hasSel = selectionLocal.Width >= 1 && selectionLocal.Height >= 1;
            if (hasSel)
            {
                var dipRect = selectionLocal;
                const double clearance = 6;
                const double gap = 10;
                if (PillOverlapsSelection(new Rect(targetLeft, targetTop, w, h), dipRect, clearance))
                {
                    var altLeft = dipRect.Right + gap;
                    var altTop = cursorLocal.Y + mouseFollowGapBelow;
                    ClampToParent(ref altLeft, ref altTop, w, h, pw, ph);
                    if (!PillOverlapsSelection(new Rect(altLeft, altTop, w, h), dipRect, clearance))
                    {
                        targetLeft = altLeft;
                        targetTop = altTop;
                    }
                    else
                    {
                        altLeft = cursorLocal.X - w / 2;
                        altTop = dipRect.Bottom + gap;
                        ClampToParent(ref altLeft, ref altTop, w, h, pw, ph);
                        if (!PillOverlapsSelection(new Rect(altLeft, altTop, w, h), dipRect, clearance))
                        {
                            targetLeft = altLeft;
                            targetTop = altTop;
                        }
                        else
                        {
                            altLeft = cursorLocal.X - w / 2;
                            altTop = dipRect.Top - h - gap;
                            ClampToParent(ref altLeft, ref altTop, w, h, pw, ph);
                            if (!PillOverlapsSelection(new Rect(altLeft, altTop, w, h), dipRect, clearance))
                            {
                                targetLeft = altLeft;
                                targetTop = altTop;
                            }
                        }
                    }
                }
            }
        }

        var k = blendAlpha ?? blendFallback;

        if (snapToTarget)
        {
            smooth.SmoothLeft = targetLeft;
            smooth.SmoothTop = targetTop;
            smooth.Initialized = true;
        }
        else if (!smooth.Initialized)
        {
            smooth.SmoothLeft = targetLeft;
            smooth.SmoothTop = targetTop;
            smooth.Initialized = true;
        }
        else
        {
            var dl = targetLeft - smooth.SmoothLeft;
            var dt = targetTop - smooth.SmoothTop;
            if (Math.Abs(dl) < snapEpsilon && Math.Abs(dt) < snapEpsilon)
            {
                smooth.SmoothLeft = targetLeft;
                smooth.SmoothTop = targetTop;
            }
            else
            {
                smooth.SmoothLeft += dl * k;
                smooth.SmoothTop += dt * k;
            }
        }

        var sl = smooth.SmoothLeft;
        var st = smooth.SmoothTop;
        ClampToParent(ref sl, ref st, w, h, pw, ph);
        smooth.SmoothLeft = sl;
        smooth.SmoothTop = st;
        left = sl;
        top = st;
    }
}
