using System.Diagnostics;

namespace ProjectStatus.Client;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly AppSettings _settings;
    private readonly ApiClient _api;
    private readonly MainForm _mainForm;
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _alwaysOnTopItem;
    private readonly ToolStripMenuItem _updateItem;
    private readonly SingleInstance _singleInstance;
    private int _activationRequested;
    private Uri? _updateUri;
    private UpdatePromptForm? _updatePrompt;
    private bool _exiting;

    public TrayApplicationContext(AppSettings settings, bool startHidden, SingleInstance singleInstance)
    {
        _settings = settings;
        _singleInstance = singleInstance;
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

        _updateItem = new ToolStripMenuItem("Update available")
        {
            Visible = false
        };
        _updateItem.Click += (_, _) => OpenUpdateDownload();

        var settingsItem = new ToolStripMenuItem("Settings");
        settingsItem.Click += async (_, _) => await _mainForm.ShowSettingsDialogAsync();

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitApplication();

        var menu = new ContextMenuStrip();
        menu.Items.Add(openItem);
        menu.Items.Add(_updateItem);
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

        // The pipe listener reports on a background thread, so the request is only recorded there
        // and applied here once the message queue goes idle.
        Application.Idle += (_, _) => ApplyPendingActivation();

        EventHandler? idleHandler = null;
        idleHandler = (_, _) =>
        {
            if (idleHandler is not null)
            {
                Application.Idle -= idleHandler;
            }

            _mainForm.StartPolling();
            _ = CheckForUpdatesAsync();
            if (!startHidden)
            {
                _mainForm.ShowFromTray();
            }

            _singleInstance.StartListening(OnActivationRequested);
        };
        Application.Idle += idleHandler;
    }

    private void OnActivationRequested()
    {
        Interlocked.Exchange(ref _activationRequested, 1);
    }

    private void ApplyPendingActivation()
    {
        if (_exiting || Interlocked.Exchange(ref _activationRequested, 0) == 0)
        {
            return;
        }

        _mainForm.ShowFromTray();
    }

    private async Task CheckForUpdatesAsync()
    {
        var update = await UpdateChecker.CheckAsync();
        if (update is null || _exiting)
        {
            return;
        }

        _updateUri = update.DownloadUri;
        _updateItem.Text = $"Update available v{update.Version}";
        _updateItem.Visible = true;
        ShowUpdatePromptIfNeeded(update);
        _notifyIcon.Text = "Project Status — update available";
    }

    // The tray item is the fallback and stays visible. The popup is shown once per newer version:
    // the offered version is remembered before the prompt appears, so dismissing it cannot make it
    // come back on the next start while the same release is still the latest one.
    private void ShowUpdatePromptIfNeeded(UpdateInfo update)
    {
        if (!UpdatePromptPolicy.ShouldPrompt(update.Version, _settings.LastUpdatePromptVersion))
        {
            return;
        }

        if (_updatePrompt is { IsDisposed: false })
        {
            return;
        }

        _settings.LastUpdatePromptVersion = update.Version;
        AppSettingsStore.Save(_settings);

        var prompt = new UpdatePromptForm(update.Version, () => ApplyUpdateAsync(update));
        prompt.UpdateLaunched += (_, _) => ExitApplication();
        prompt.FormClosed += (_, _) => _updatePrompt = null;
        _updatePrompt = prompt;
        prompt.Show();
    }

    // The installer is downloaded to a temporary directory, checked against the checksum published
    // with the release, and only then started. Nothing is launched on a mismatch, and every failure
    // leaves the running application alone so the update can be tried again later.
    private async Task<string?> ApplyUpdateAsync(UpdateInfo update)
    {
        if (update.InstallerUri is null || update.ChecksumUri is null)
        {
            return "This release has no installer to update from. Use the tray item to open the release page.";
        }

        string installerPath;
        string checksumText;
        try
        {
            installerPath = await UpdateDownloader.DownloadAsync(update.InstallerUri);
            checksumText = await UpdateDownloader.DownloadTextAsync(update.ChecksumUri);
        }
        catch (Exception ex)
        {
            return $"The update could not be downloaded: {ex.Message}";
        }

        var expectedHash = UpdateChecksum.ParseHash(checksumText, update.InstallerFileName);
        if (expectedHash is null)
        {
            return "Update verification failed.";
        }

        string actualHash;
        try
        {
            actualHash = await UpdateDownloader.ComputeFileHashAsync(installerPath);
        }
        catch (Exception ex)
        {
            return $"Update verification failed: {ex.Message}";
        }

        if (!UpdateChecksum.Matches(expectedHash, actualHash))
        {
            return "Update verification failed.";
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = installerPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            return $"The installer could not be started: {ex.Message}";
        }

        // The installer was started, so the running application gets out of its way.
        return null;
    }

    private void OpenUpdateDownload()
    {
        if (_updateUri is null)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _updateUri.AbsoluteUri,
                UseShellExecute = true
            });
        }
        catch
        {
            // Update checks are a convenience and must never break the running app.
        }
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
        _updatePrompt?.Close();
        _mainForm.AllowExitAndClose();
    }

    protected override void ExitThreadCore()
    {
        _updatePrompt?.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _api.Dispose();
        base.ExitThreadCore();
    }
}
