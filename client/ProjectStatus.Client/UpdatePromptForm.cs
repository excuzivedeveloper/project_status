namespace ProjectStatus.Client;

// Small non-modal prompt shown when a newer release is found. It never opens the main window:
// the tray item stays the fallback, and closing this dialog leaves it in place.
internal sealed class UpdatePromptForm : Form
{
    public event EventHandler? UpdateRequested;

    public UpdatePromptForm(string version)
    {
        Text = "Project Status update";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        TopMost = true;
        ClientSize = new Size(360, 132);

        var title = new Label
        {
            Text = "Project Status update",
            Font = new Font(Font, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(16, 16)
        };

        var message = new Label
        {
            Text = $"Version {version} is available.",
            AutoSize = true,
            Location = new Point(16, 46)
        };

        var updateButton = new Button
        {
            Text = "Update",
            Size = new Size(88, 28),
            Location = new Point(ClientSize.Width - 16 - 88, ClientSize.Height - 16 - 28)
        };
        updateButton.Click += (_, _) =>
        {
            Close();
            UpdateRequested?.Invoke(this, EventArgs.Empty);
        };

        var laterButton = new Button
        {
            Text = "Later",
            Size = new Size(88, 28),
            Location = new Point(ClientSize.Width - 16 - 88 - 8 - 88, ClientSize.Height - 16 - 28)
        };
        laterButton.Click += (_, _) => Close();

        Controls.Add(title);
        Controls.Add(message);
        Controls.Add(updateButton);
        Controls.Add(laterButton);

        AcceptButton = updateButton;
        CancelButton = laterButton;
    }
}
