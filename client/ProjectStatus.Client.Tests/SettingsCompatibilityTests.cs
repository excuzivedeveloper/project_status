using ProjectStatus.Client;
using Xunit;

namespace ProjectStatus.Client.Tests;

// An upgrade has to keep the settings file that already exists. The JSON below is what v0.1.3 wrote:
// it has none of the v0.1.4 properties, and loading it must not lose anything or reset old values.
public class SettingsCompatibilityTests
{
    private const string LegacySettingsJson = """
    {
      "ServerAddress": "http://example.invalid:8080",
      "LocalDeviceName": "Desk",
      "AlwaysOnTop": true,
      "StartWithWindows": false,
      "LastUpdatePromptVersion": "0.1.3",
      "WindowX": 10,
      "WindowY": 20,
      "WindowWidth": 700,
      "WindowHeight": 320
    }
    """;

    [Fact]
    public void Existing_values_survive_an_upgrade()
    {
        var settings = AppSettingsStore.FromJson(LegacySettingsJson);

        Assert.Equal("http://example.invalid:8080", settings.ServerAddress);
        Assert.Equal("Desk", settings.LocalDeviceName);
        Assert.True(settings.AlwaysOnTop);
        Assert.False(settings.StartWithWindows);
        Assert.Equal("0.1.3", settings.LastUpdatePromptVersion);
        Assert.Equal(10, settings.WindowX);
        Assert.Equal(20, settings.WindowY);
        Assert.Equal(700, settings.WindowWidth);
        Assert.Equal(320, settings.WindowHeight);
    }

    [Fact]
    public void New_properties_default_to_full_mode_and_default_appearance()
    {
        var settings = AppSettingsStore.FromJson(LegacySettingsJson);

        Assert.False(settings.CompactMode);
        Assert.Equal(AppearanceSettings.DefaultOpacityPercent, settings.CompactOpacity);
        Assert.Equal(100, settings.CompactOpacity);
        Assert.Equal(AppearanceSettings.DefaultBackgroundColor, settings.BackgroundColor);
        Assert.Equal(string.Empty, settings.BackgroundColor);

        // A settings file written before the language existed is treated as Auto.
        Assert.Equal(Localization.AutoValue, settings.Language);
    }

    [Fact]
    public void Window_visibility_starts_hidden_for_a_file_that_never_recorded_it()
    {
        // v0.1.3 always started hidden with --startup, so a legacy file keeps that behaviour until the
        // user opens or hides the window once.
        Assert.False(AppSettingsStore.FromJson(LegacySettingsJson).MainWindowVisible);
    }

    [Fact]
    public void Compact_geometry_defaults_without_touching_the_full_one()
    {
        var settings = AppSettingsStore.FromJson(LegacySettingsJson);

        Assert.Null(settings.CompactWindowX);
        Assert.Null(settings.CompactWindowY);
        Assert.Equal(240, settings.CompactWindowWidth);
        Assert.Equal(300, settings.CompactWindowHeight);

        Assert.Equal(10, settings.WindowX);
        Assert.Equal(700, settings.WindowWidth);
    }

    [Fact]
    public void Round_trip_keeps_every_mode_and_appearance_value()
    {
        var settings = new AppSettings
        {
            ServerAddress = "http://example.invalid:8080",
            LocalDeviceName = "Desk",
            AlwaysOnTop = true,
            MainWindowVisible = true,
            CompactMode = true,
            CompactOpacity = 85,
            BackgroundColor = "#123456",
            Language = Localization.RussianValue,
            CompactWindowX = 40,
            CompactWindowY = 50,
            CompactWindowWidth = 260,
            CompactWindowHeight = 320,
            WindowX = 700,
            WindowY = 120,
            WindowWidth = 760,
            WindowHeight = 360
        };

        var restored = AppSettingsStore.FromJson(AppSettingsStore.ToJson(settings));

        Assert.Equal("http://example.invalid:8080", restored.ServerAddress);
        Assert.True(restored.MainWindowVisible);
        Assert.True(restored.CompactMode);
        Assert.Equal(85, restored.CompactOpacity);
        Assert.Equal("#123456", restored.BackgroundColor);
        Assert.Equal(Localization.RussianValue, restored.Language);
        Assert.Equal(40, restored.CompactWindowX);
        Assert.Equal(260, restored.CompactWindowWidth);
        Assert.Equal(700, restored.WindowX);
        Assert.Equal(760, restored.WindowWidth);

        // Switching modes must not disturb the other geometry.
        Assert.NotEqual(restored.WindowX, restored.CompactWindowX);
    }

    [Fact]
    public void An_empty_or_broken_document_falls_back_to_defaults()
    {
        Assert.True(AppSettingsStore.FromJson("{}").IsConfigured == false);
        Assert.Equal(100, AppSettingsStore.FromJson("{}").CompactOpacity);
        Assert.Equal(string.Empty, AppSettingsStore.FromJson("{}").BackgroundColor);
    }
}
