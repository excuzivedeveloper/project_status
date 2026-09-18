namespace ProjectStatus.Client;

// Small non-modal prompt shown when a newer release is found. It never opens the main window: the
// tray item stays the fallback, and closing the dialog leaves it in place.
internal sealed class UpdatePromptForm : Form
{
    private readonly Label _message;
    private readonly Button _updateButton;
    private readonly Button _laterButton;
    private readonly Func<Task<string?>> _applyUpdate;

    // Raised after the installer has been started, which is the point where the application has to
    // shut down so the installer can replace it.
    public event EventHandler? UpdateLaunched;

    public UpdatePromptForm(string version, Func<Task<string?>> applyUpdate)
    {
        _applyUpdate = applyUpdate;

        Text = "Project Status update";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        TopMost = true;
        ClientSize = new Size(400, 148);

        var title = new Label
        {
            Text = "Project Status update",
            Font = new Font(Font, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(16, 16)
        };

        _message = new Label
        {
            Text = $"Version {version} is available.",
            AutoSize = true,
            Location = new Point(16, 48),
            MaximumSize = new Size(ClientSize.Width - 32, 0)
        };

        _updateButton = new Button
        {
            Text = "Update now",
            Size = new Size(104, 28),
            Location = new Point(ClientSize.Width - 16 - 104, ClientSize.Height - 16 - 28)
        };
        _updateButton.Click += async (_, _) => await ApplyUpdateAsync();

        _laterButton = new Button
        {
            Text = "Later",
            Size = new Size(88, 28),
            Location = new Point(ClientSize.Width - 16 - 104 - 8 - 88, ClientSize.Height - 16 - 28)
        };
        _laterButton.Click += (_, _) => Close();

        Controls.Add(title);
        Controls.Add(_message);
        Controls.Add(_updateButton);
        Controls.Add(_laterButton);

        AcceptButton = _updateButton;
        CancelButton = _laterButton;
    }

    // The download, checksum check and installer launch happen outside the form. A null result means
    // the installer was started and the application should now shut down; anything else is shown to
    // the user and the dialog stays usable, so the update can be retried later.
    private async Task ApplyUpdateAsync()
    {
        _updateButton.Enabled = false;
        _laterButton.Enabled = false;
        _message.Text = "Downloading update...";

        string? error;
        try
        {
            error = await _applyUpdate();
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        if (error is null)
        {
            UpdateLaunched?.Invoke(this, EventArgs.Empty);
            Close();
            return;
        }

        _message.Text = error;
        _updateButton.Enabled = true;
        _laterButton.Enabled = true;
    }
}
