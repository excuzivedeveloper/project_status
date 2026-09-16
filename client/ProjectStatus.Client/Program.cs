namespace ProjectStatus.Client;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var settings = AppSettingsStore.Load();
        if (!settings.IsConfigured)
        {
            using var setup = new FirstRunForm(settings);
            if (setup.ShowDialog() != DialogResult.OK)
            {
                return;
            }

            AppSettingsStore.Save(settings);
        }

        AutostartManager.Apply(settings.StartWithWindows);
        var startHidden = args.Any(arg => string.Equals(arg, "--startup", StringComparison.OrdinalIgnoreCase));
        Application.Run(new TrayApplicationContext(settings, startHidden));
    }
}
