namespace ProjectStatus.Client;

// Chooses the rectangle a window should use when a mode is restored or shown for the first time.
// Kept free of UI types so the placement rules can be checked without creating a window.
internal static class WindowBounds
{
    private const int DefaultMargin = 24;

    public static Rectangle Resolve(
        int? savedX,
        int? savedY,
        int width,
        int height,
        Size minimum,
        Rectangle primaryWorkingArea,
        IReadOnlyList<Rectangle> workingAreas)
    {
        var targetWidth = Math.Max(minimum.Width, width);
        var targetHeight = Math.Max(minimum.Height, height);

        if (savedX is int x && savedY is int y)
        {
            var saved = new Rectangle(x, y, targetWidth, targetHeight);
            var area = FindWorkingArea(saved, workingAreas);

            if (area is Rectangle target)
            {
                // The saved rectangle is only a wish: a window is always fitted into the working area
                // it belongs to, so it never ends up partly or entirely outside the screen.
                return FitInside(saved, target, minimum);
            }
        }

        // No saved geometry, or the monitor it was saved on is gone: the default corner is used and
        // still fitted into the primary working area.
        var defaultBounds = new Rectangle(
            primaryWorkingArea.Right - targetWidth - DefaultMargin,
            primaryWorkingArea.Top + DefaultMargin,
            targetWidth,
            targetHeight);

        return FitInside(defaultBounds, primaryWorkingArea, minimum);
    }

    // The window's own corner decides which monitor it belongs to, so a window that merely peeks onto
    // a screen is not adopted by it. When the corner is outside every screen, the monitor with the
    // largest overlap is the best remaining candidate; no overlap at all means the saved location
    // does not relate to any existing monitor.
    private static Rectangle? FindWorkingArea(Rectangle saved, IReadOnlyList<Rectangle> workingAreas)
    {
        foreach (var area in workingAreas)
        {
            if (area.Contains(saved.Location))
            {
                return area;
            }
        }

        Rectangle? best = null;
        var bestOverlap = 0;

        foreach (var area in workingAreas)
        {
            var overlap = Rectangle.Intersect(area, saved);
            var overlapArea = overlap.Width * overlap.Height;
            if (overlapArea > bestOverlap)
            {
                bestOverlap = overlapArea;
                best = area;
            }
        }

        return best;
    }

    private static Rectangle FitInside(Rectangle desired, Rectangle area, Size minimum)
    {
        var width = Math.Min(Math.Max(minimum.Width, desired.Width), area.Width);
        var height = Math.Min(Math.Max(minimum.Height, desired.Height), area.Height);

        var x = Math.Min(Math.Max(desired.X, area.Left), area.Right - width);
        var y = Math.Min(Math.Max(desired.Y, area.Top), area.Bottom - height);

        return new Rectangle(x, y, width, height);
    }
}
