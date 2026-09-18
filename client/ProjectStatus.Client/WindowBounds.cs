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

        // Saved geometry is used only when the window would still land on a screen. After a monitor
        // change the old coordinates can point at nothing, and a window nobody can reach is worse
        // than a window in the default corner.
        if (savedX is int x && savedY is int y)
        {
            var saved = new Rectangle(x, y, targetWidth, targetHeight);
            if (workingAreas.Any(area => area.IntersectsWith(saved)))
            {
                return saved;
            }
        }

        var clampedWidth = Math.Min(targetWidth, primaryWorkingArea.Width);
        var clampedHeight = Math.Min(targetHeight, primaryWorkingArea.Height);

        return new Rectangle(
            primaryWorkingArea.Right - clampedWidth - DefaultMargin,
            primaryWorkingArea.Top + DefaultMargin,
            clampedWidth,
            clampedHeight);
    }
}
