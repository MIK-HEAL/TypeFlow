using System.Windows;

namespace TypeFlow.Desktop;

public static class CandidatePlacement
{
    // All arguments use physical pixels. Keep the complete skin outside the text line.
    public static Point Locate(Rect caret, Rect work, Size size, double gap, string placement)
    {
        var width = Math.Min(size.Width, work.Width);
        var height = Math.Min(size.Height, work.Height);
        var x = Math.Clamp(caret.Left, work.Left, work.Right - width);
        var below = new Point(x, caret.Bottom + gap);
        var right = new Point(caret.Right + gap, Math.Clamp(caret.Top, work.Top, work.Bottom - height));
        var left = new Point(caret.Left - gap - width, right.Y);
        bool Fits(Point p) => work.Contains(new Rect(p, new Size(width, height)));
        if (placement == "bottom") return new(work.Left + (work.Width - width) / 2, work.Bottom - height - Math.Min(gap, work.Height - height));
        if (placement == "right" && Fits(right)) return right;
        if (Fits(below)) return below;
        if (Fits(right)) return right;
        if (Fits(left)) return left;
        var above = new Point(x, caret.Top - gap - height);
        if (Fits(above)) return above;
        // An oversized skin may leave no non-overlapping placement on this monitor.
        return new(x, Math.Clamp(below.Y, work.Top, work.Bottom - height));
    }
}
