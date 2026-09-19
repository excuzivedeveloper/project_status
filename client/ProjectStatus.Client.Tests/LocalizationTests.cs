using System.Collections;
using System.Globalization;
using System.Resources;
using ProjectStatus.Client;
using Xunit;

namespace ProjectStatus.Client.Tests;

// The language decision is a pure rule table, and the resource files are checked against each other
// so a missing translation cannot reach a release.
public class LocalizationTests
{
    private static readonly CultureInfo RussianSystem = new("ru-RU");
    private static readonly CultureInfo EnglishSystem = new("en-US");

    [Theory]
    [InlineData("ru", "ru")]
    [InlineData("RU", "ru")]
    [InlineData(" en ", "")]
    [InlineData("en", "")]
    [InlineData("de", "")]
    [InlineData("something-else", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Resolve_reads_an_explicit_language(string? requested, string expected)
    {
        Assert.Equal(expected, Localization.Resolve(requested, EnglishSystem).Name);
    }

    [Fact]
    public void Auto_follows_the_operating_system_language()
    {
        Assert.Equal("ru", Localization.Resolve("auto", RussianSystem).Name);
        Assert.Equal("ru", Localization.Resolve(null, RussianSystem).Name);
        Assert.Equal("ru", Localization.Resolve("auto", new CultureInfo("ru-KZ")).Name);
        Assert.Equal(string.Empty, Localization.Resolve("auto", EnglishSystem).Name);
        Assert.Equal(string.Empty, Localization.Resolve(null, EnglishSystem).Name);
    }

    [Fact]
    public void Stored_values_round_trip()
    {
        foreach (var preference in new[]
                 {
                     LanguagePreference.Auto,
                     LanguagePreference.English,
                     LanguagePreference.Russian
                 })
        {
            Assert.Equal(preference, Localization.FromStoredValue(Localization.ToStoredValue(preference)));
        }
    }

    [Fact]
    public void Unknown_stored_values_fall_back_to_auto()
    {
        Assert.Equal(LanguagePreference.Auto, Localization.FromStoredValue(null));
        Assert.Equal(LanguagePreference.Auto, Localization.FromStoredValue(string.Empty));
        Assert.Equal(LanguagePreference.Auto, Localization.FromStoredValue("klingon"));
    }

    [Fact]
    public void Switching_language_changes_the_interface_text()
    {
        try
        {
            Localization.Apply("en");
            Assert.Equal("Settings", Strings.ToolbarSettings);
            Assert.Equal("Projects", Strings.TabProjects);
            Assert.Equal("This PC: Desk", Strings.DeviceLabelFormat("Desk"));

            Localization.Apply("ru");
            Assert.Equal("Настройки", Strings.ToolbarSettings);
            Assert.Equal("Проекты", Strings.TabProjects);
            Assert.Equal("Этот компьютер: Desk", Strings.DeviceLabelFormat("Desk"));
        }
        finally
        {
            Localization.Apply(null);
        }
    }

    [Fact]
    public void User_data_is_never_translated()
    {
        try
        {
            Localization.Apply("ru");

            // Only the format is localised; the project and device names are passed through untouched.
            Assert.Contains("Мой проект", Strings.ConfirmDeleteProjectFormat("Мой проект"));
            Assert.Contains("My project", Strings.ConfirmDeleteProjectFormat("My project"));
            Assert.Contains("Desk-01", Strings.DeviceLabelFormat("Desk-01"));
        }
        finally
        {
            Localization.Apply(null);
        }
    }

    [Fact]
    public void Russian_covers_every_english_key()
    {
        var manager = new ResourceManager("ProjectStatus.Client.Resources.Strings", typeof(Localization).Assembly);

        var english = manager.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: true);
        var russian = manager.GetResourceSet(new CultureInfo("ru"), createIfNotExists: true, tryParents: false);

        Assert.NotNull(english);
        Assert.NotNull(russian);

        var englishKeys = Keys(english!).OrderBy(key => key, StringComparer.Ordinal).ToList();
        var russianKeys = Keys(russian!).OrderBy(key => key, StringComparer.Ordinal).ToList();

        Assert.True(englishKeys.Count > 50, $"only {englishKeys.Count} keys found");
        Assert.Equal(englishKeys, russianKeys);
    }

    [Fact]
    public void A_missing_key_falls_back_to_the_key()
    {
        Assert.Equal("NoSuchStringExists", Localization.Get("NoSuchStringExists"));
    }

    private static IEnumerable<string> Keys(ResourceSet set)
    {
        foreach (DictionaryEntry entry in set)
        {
            yield return (string)entry.Key;
        }
    }
}
