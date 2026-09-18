using System.Text.Json;
using Microsoft.Win32;

namespace ProjectStatus.Client;

internal sealed class AppSettings
{
    public string ServerAddress { get; set; } = string.Empty;
    public string LocalDeviceName { get; set; } = string.Empty;
    public bool AlwaysOnTop { get; set; }
    public bool StartWithWindows { get; set; } = true;

    // Whether the main window was open when the app last stopped. A settings file written before
    // this property existed deserializes to false, which keeps the previous startup behaviour
    // (start hidden) until the user opens or hides the window once.
    public bool MainWindowVisible { get; set; }

    // Compact keeps its own geometry, so switching modes leaves the other layout where it was.
    public bool CompactMode { get; set; }
    public int? CompactWindowX { get; set; }
    public int? CompactWindowY { get; set; }
    public int CompactWindowWidth { get; set; } = 240;
    public int CompactWindowHeight { get; set; } = 300;

    // Appearance. CompactOpacity only affects Compact mode; Full is always fully opaque. An empty
    // background means the system surface colours.
    public int CompactOpacity { get; set; } = AppearanceSettings.DefaultOpacityPercent;
    public string BackgroundColor { get; set; } = AppearanceSettings.DefaultBackgroundColor;

    public string LastUpdatePromptVersion { get; set; } = string.Empty;

    // Full mode geometry. These four keep their original names so an existing settings.json loads
    // unchanged.
    public int? WindowX { get; set; }
    public int? WindowY { get; set; }
    public int WindowWidth { get; set; } = 760;
    public int WindowHeight { get; set; } = 360;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ServerAddress) &&
        !string.IsNullOrWhiteSpace(LocalDeviceName);

    public static string NormalizeServerAddress(string value)
    {
        value = value.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Server address is required.");
        }

        if (!value.Contains("://", StringComparison.Ordinal))
        {
            value = "http://" + value;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new ArgumentException("Enter a valid HTTP or HTTPS server address.");
        }

        return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
    }
}

internal static class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static string SettingsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProjectStatus");

    private static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new AppSettings();
            }

            return FromJson(File.ReadAllText(SettingsPath));
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(SettingsDirectory);
        File.WriteAllText(SettingsPath, ToJson(settings));
    }

    // Serialisation is exposed so the settings contract can be checked without touching the real
    // settings file: a file written by an older version has to load with the new defaults.
    internal static AppSettings FromJson(string json)
    {
        return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
    }

    internal static string ToJson(AppSettings settings)
    {
        return JsonSerializer.Serialize(settings, JsonOptions);
    }
}

internal static class AutostartManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Project Status";

    public static void Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

            if (enabled)
            {
                key.SetValue(ValueName, $"\"{Application.ExecutablePath}\" --startup");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // Autostart is a convenience. Failure must not prevent the app from running.
        }
    }
}
