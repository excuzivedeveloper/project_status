namespace ProjectStatus.Client;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        // Claim the per-user instance slot first, so two simultaneous launches cannot both open a
        // window, a tray icon or the first-run dialog.
        using var singleInstance = SingleInstance.Acquire();
        var isStartupLaunch = args.Any(arg => string.Equals(arg, "--startup", StringComparison.OrdinalIgnoreCase));

        if (!singleInstance.IsPrimaryInstance)
        {
            // A manual second launch brings the running window forward. A repeated --startup only
            // exits: a background start must never pull a hidden window back on screen.
            if (!isStartupLaunch)
            {
                SingleInstance.RequestActivation();
            }

            return;
        }

        var settings = AppSettingsStore.Load();

        // Pick the interface language before any window is created.
        Localization.Apply(settings.Language);

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
        CoreUiBehavior.Install();

        // A manual launch always shows the window. --startup restores what the previous session left
        // behind: open when the window was open, hidden when it was in the tray.
        var startHidden = isStartupLaunch && !settings.MainWindowVisible;
        Application.Run(new TrayApplicationContext(settings, startHidden, singleInstance));
    }
}
