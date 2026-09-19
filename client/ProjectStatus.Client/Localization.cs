using System.Globalization;
using System.Resources;

namespace ProjectStatus.Client;

internal enum LanguagePreference
{
    Auto,
    English,
    Russian
}

// The interface language. English is the neutral resource, Russian is a satellite resource, and Auto
// follows the operating system. The stored value is a short string so settings.json stays readable;
// anything unknown falls back to Auto.
internal static class Localization
{
    public const string AutoValue = "auto";
    public const string EnglishValue = "en";
    public const string RussianValue = "ru";

    private static readonly ResourceManager Manager =
        new("ProjectStatus.Client.Resources.Strings", typeof(Localization).Assembly);

    private static CultureInfo _culture = CultureInfo.InvariantCulture;

    public static CultureInfo Culture => _culture;

    public static void Apply(string? language)
    {
        _culture = Resolve(language, CultureInfo.CurrentUICulture);
    }

    public static string Get(string key)
    {
        return Manager.GetString(key, _culture)
               ?? Manager.GetString(key, CultureInfo.InvariantCulture)
               ?? key;
    }

    public static string Format(string key, params object[] arguments)
    {
        return string.Format(_culture, Get(key), arguments);
    }

    // Pure rule table, so the language decision can be checked without touching the real OS culture.
    public static CultureInfo Resolve(string? language, CultureInfo operatingSystemCulture)
    {
        switch ((language ?? AutoValue).Trim().ToLowerInvariant())
        {
            case RussianValue:
                return new CultureInfo("ru");

            case EnglishValue:
                return CultureInfo.InvariantCulture;

            case "":
            case AutoValue:
                return operatingSystemCulture.TwoLetterISOLanguageName.Equals("ru", StringComparison.OrdinalIgnoreCase)
                    ? new CultureInfo("ru")
                    : CultureInfo.InvariantCulture;

            default:
                return CultureInfo.InvariantCulture;
        }
    }

    public static string ToStoredValue(LanguagePreference preference)
    {
        return preference switch
        {
            LanguagePreference.Russian => RussianValue,
            LanguagePreference.English => EnglishValue,
            _ => AutoValue
        };
    }

    public static LanguagePreference FromStoredValue(string? value)
    {
        return (value ?? AutoValue).Trim().ToLowerInvariant() switch
        {
            RussianValue => LanguagePreference.Russian,
            EnglishValue => LanguagePreference.English,
            _ => LanguagePreference.Auto
        };
    }
}
