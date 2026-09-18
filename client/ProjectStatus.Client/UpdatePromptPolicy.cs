namespace ProjectStatus.Client;

// Decides whether a newer version should be announced with a popup.
// Kept free of UI and network code so the rule can be exercised on its own.
internal static class UpdatePromptPolicy
{
    public static bool ShouldPrompt(string? offeredVersion, string? lastPromptedVersion)
    {
        var offered = ParseVersion(offeredVersion);
        if (offered is null)
        {
            return false;
        }

        var last = ParseVersion(lastPromptedVersion);

        // Nothing remembered yet: this is the first time the version is offered.
        // Otherwise only a version newer than the one already announced is worth another popup.
        return last is null || offered.CompareTo(last) > 0;
    }

    private static Version? ParseVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();
        if (text.StartsWith('v') || text.StartsWith('V'))
        {
            text = text[1..];
        }

        var separator = text.IndexOfAny(['-', '+']);
        if (separator >= 0)
        {
            text = text[..separator];
        }

        return Version.TryParse(text, out var parsed)
            ? new Version(
                Math.Max(parsed.Major, 0),
                Math.Max(parsed.Minor, 0),
                Math.Max(parsed.Build, 0))
            : null;
    }
}
