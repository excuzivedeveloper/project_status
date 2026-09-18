namespace ProjectStatus.Client;

// Appearance rules, kept free of UI types so clamping, parsing and defaults can be checked without
// creating a window.
internal static class AppearanceSettings
{
    public const int MinimumOpacityPercent = 70;
    public const int MaximumOpacityPercent = 100;
    public const int DefaultOpacityPercent = 100;

    // Empty means "use the system surface colours".
    public const string DefaultBackgroundColor = "";

    public static int NormalizeOpacityPercent(int percent)
    {
        return Math.Clamp(percent, MinimumOpacityPercent, MaximumOpacityPercent);
    }

    public static double ToOpacity(int percent)
    {
        return NormalizeOpacityPercent(percent) / 100.0;
    }

    public static Color ParseBackgroundColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Color.Empty;
        }

        try
        {
            // Server-validated status colours use the same format; malformed legacy values fall back
            // to the default appearance instead of breaking the window.
            return ColorTranslator.FromHtml(value.Trim());
        }
        catch
        {
            return Color.Empty;
        }
    }

    public static string ToHex(Color color)
    {
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }
}
