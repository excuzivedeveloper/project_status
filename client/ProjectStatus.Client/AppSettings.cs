using System.Text.Json;
using Microsoft.Win32;

namespace ProjectStatus.Client;

internal sealed class AppSettings
{
    public string ServerAddress { get; set; } = string.Empty;
    public string LocalDeviceName { get; set; } = string.Empty;
    public bool AlwaysOnTop { get; set; }
    public bool StartWithWindows { get; set; } = true;
    public string LastUpdatePromptVersion { get; set; } = string.Empty;
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

            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(SettingsDirectory);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsPath, json);
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
