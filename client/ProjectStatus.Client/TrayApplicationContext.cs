namespace ProjectStatus.Client;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly AppSettings _settings;
    private readonly ApiClient _api;
    private readonly MainForm _mainForm;
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _alwaysOnTopItem;
    private bool _exiting;

    public TrayApplicationContext(AppSettings settings, bool startHidden)
    {
        _settings = settings;
        _api = new ApiClient(settings.ServerAddress);
        _mainForm = new MainForm(settings, _api);
        MainForm = _mainForm;

        _alwaysOnTopItem = new ToolStripMenuItem("Always on top")
        {
            Checked = settings.AlwaysOnTop,
            CheckOnClick = false
        };
        _alwaysOnTopItem.Click += (_, _) => _mainForm.SetAlwaysOnTop(!_mainForm.TopMost);

        var openItem = new ToolStripMenuItem("Open");
        openItem.Click += (_, _) => _mainForm.ShowFromTray();

        var settingsItem = new ToolStripMenuItem("Settings");
        settingsItem.Click += (_, _) => _mainForm.ShowSettingsDialog();

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitApplication();

        var menu = new ContextMenuStrip();
        menu.Items.Add(openItem);
        menu.Items.Add(_alwaysOnTopItem);
        menu.Items.Add(settingsItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        _notifyIcon = new NotifyIcon
        {
            Icon = icon,
            Text = "Project Status",
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => _mainForm.ShowFromTray();

        _mainForm.AlwaysOnTopChanged += (_, _) =>
        {
            _alwaysOnTopItem.Checked = _mainForm.TopMost;
        };

        EventHandler? idleHandler = null;
        idleHandler = (_, _) =>
        {
            if (idleHandler is not null)
            {
                Application.Idle -= idleHandler;
            }

            _mainForm.StartPolling();
            if (!startHidden)
            {
                _mainForm.ShowFromTray();
            }
        };
        Application.Idle += idleHandler;
    }

    private void ExitApplication()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        _mainForm.CaptureWindowSettings();
        AppSettingsStore.Save(_settings);
        _notifyIcon.Visible = false;
        _mainForm.AllowExitAndClose();
    }

    protected override void ExitThreadCore()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _api.Dispose();
        base.ExitThreadCore();
    }
}
