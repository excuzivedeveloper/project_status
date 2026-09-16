namespace ProjectStatus.Client;

internal sealed class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly TextBox _serverText;
    private readonly ComboBox _localDeviceCombo;
    private readonly CheckBox _alwaysOnTopCheck;
    private readonly CheckBox _autostartCheck;
    private readonly ListBox _statusesList;
    private readonly ListBox _devicesList;
    private readonly Label _connectionLabel;

    public SettingsForm(AppSettings settings, StateSnapshot state)
    {
        _settings = settings;

        Text = "Project Status — Settings";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(520, 500);
        Size = new Size(560, 560);
        MaximizeBox = false;
        MinimizeBox = false;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 6,
            Padding = new Padding(12)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(new Label { Text = "Server:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        _serverText = new TextBox { Dock = DockStyle.Fill, Text = settings.ServerAddress };
        root.Controls.Add(_serverText, 1, 0);

        root.Controls.Add(new Label { Text = "This computer:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        _localDeviceCombo = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDown,
            Text = settings.LocalDeviceName
        };
        foreach (var device in state.Devices)
        {
            _localDeviceCombo.Items.Add(device.Name);
        }
        root.Controls.Add(_localDeviceCombo, 1, 1);

        var options = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        _alwaysOnTopCheck = new CheckBox { Text = "Always on top", Checked = settings.AlwaysOnTop, AutoSize = true };
        _autostartCheck = new CheckBox { Text = "Start with Windows", Checked = settings.StartWithWindows, AutoSize = true };
        options.Controls.Add(_alwaysOnTopCheck);
        options.Controls.Add(_autostartCheck);
        root.Controls.Add(options, 1, 2);

        var connectionPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight
        };
        var testButton = new Button { Text = "Test connection", AutoSize = true };
        testButton.Click += async (_, _) => await TestConnectionAsync();
        _connectionLabel = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(4, 7, 0, 0) };
        connectionPanel.Controls.Add(testButton);
        connectionPanel.Controls.Add(_connectionLabel);
        root.Controls.Add(connectionPanel, 1, 3);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        _statusesList = new ListBox { Dock = DockStyle.Fill };
        _devicesList = new ListBox { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildStatusesTab());
        tabs.TabPages.Add(BuildDevicesTab());
        root.Controls.Add(tabs, 0, 4);
        root.SetColumnSpan(tabs, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft
        };
        var saveButton = new Button { Text = "Save", DialogResult = DialogResult.None, AutoSize = true };
        saveButton.Click += (_, _) => SaveAndClose();
        var cancelButton = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.Add(saveButton);
        buttons.Controls.Add(cancelButton);
        root.Controls.Add(buttons, 0, 5);
        root.SetColumnSpan(buttons, 2);

        Controls.Add(root);
        AcceptButton = saveButton;
        CancelButton = cancelButton;

        Shown += async (_, _) => await ReloadSharedAsync(showError: false);
    }

    private TabPage BuildStatusesTab()
    {
        var page = new TabPage("Statuses");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(6)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(_statusesList, 0, 0);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        buttons.Controls.Add(ActionButton("Add", async () => await AddStatusAsync()));
        buttons.Controls.Add(ActionButton("Rename", async () => await RenameStatusAsync()));
        buttons.Controls.Add(ActionButton("Color", async () => await ChangeStatusColorAsync()));
        buttons.Controls.Add(ActionButton("Delete", async () => await DeleteStatusAsync()));
        layout.Controls.Add(buttons, 0, 1);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildDevicesTab()
    {
        var page = new TabPage("Devices");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(6)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(_devicesList, 0, 0);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        buttons.Controls.Add(ActionButton("Add", async () => await AddDeviceAsync()));
        buttons.Controls.Add(ActionButton("Rename", async () => await RenameDeviceAsync()));
        buttons.Controls.Add(ActionButton("Delete", async () => await DeleteDeviceAsync()));
        layout.Controls.Add(buttons, 0, 1);
        page.Controls.Add(layout);
        return page;
    }

    private static Button ActionButton(string text, Func<Task> action)
    {
        var button = new Button { Text = text, AutoSize = true };
        button.Click += async (_, _) => await action();
        return button;
    }

    private async Task TestConnectionAsync()
    {
        try
        {
            using var api = CreateApiFromField();
            await api.GetStateAsync();
            _connectionLabel.Text = "OK";
            _connectionLabel.ForeColor = Color.DarkGreen;
        }
        catch (Exception ex)
        {
            _connectionLabel.Text = "Failed";
            _connectionLabel.ForeColor = Color.Firebrick;
            ShowError(ex.Message);
        }
    }

    private async Task ReloadSharedAsync(bool showError = true)
    {
        try
        {
            using var api = CreateApiFromField();
            var state = await api.GetStateAsync();

            var localText = _localDeviceCombo.Text;
            _statusesList.Items.Clear();
            foreach (var status in state.Statuses)
            {
                _statusesList.Items.Add(status);
            }

            _devicesList.Items.Clear();
            _localDeviceCombo.Items.Clear();
            foreach (var device in state.Devices)
            {
                _devicesList.Items.Add(device);
                _localDeviceCombo.Items.Add(device.Name);
            }
            _localDeviceCombo.Text = localText;
        }
        catch (Exception ex)
        {
            if (showError)
            {
                ShowError(ex.Message);
            }
        }
    }

    private async Task AddStatusAsync()
    {
        var name = PromptDialog.Show(this, "Add status", "Status name:");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        using var colorDialog = new ColorDialog { Color = Color.SlateGray, FullOpen = true };
        if (colorDialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        await RunSharedMutationAsync(api => api.CreateStatusAsync(new StatusPayload
        {
            Name = name.Trim(),
            Color = ToHex(colorDialog.Color)
        }));
    }

    private async Task RenameStatusAsync()
    {
        if (_statusesList.SelectedItem is not StatusDto status)
        {
            return;
        }

        var name = PromptDialog.Show(this, "Rename status", "Status name:", status.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        await RunSharedMutationAsync(api => api.UpdateStatusAsync(status.Id, new StatusPayload
        {
            Name = name.Trim(),
            Color = status.Color
        }));
    }

    private async Task ChangeStatusColorAsync()
    {
        if (_statusesList.SelectedItem is not StatusDto status)
        {
            return;
        }

        using var colorDialog = new ColorDialog { FullOpen = true };
        try
        {
            colorDialog.Color = ColorTranslator.FromHtml(status.Color);
        }
        catch
        {
            colorDialog.Color = Color.SlateGray;
        }

        if (colorDialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        await RunSharedMutationAsync(api => api.UpdateStatusAsync(status.Id, new StatusPayload
        {
            Name = status.Name,
            Color = ToHex(colorDialog.Color)
        }));
    }

    private async Task DeleteStatusAsync()
    {
        if (_statusesList.SelectedItem is not StatusDto status)
        {
            return;
        }

        if (MessageBox.Show(this, $"Delete status '{status.Name}'?", "Project Status",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }

        await RunSharedMutationAsync(async api =>
        {
            await api.DeleteStatusAsync(status.Id);
            return status;
        });
    }

    private async Task AddDeviceAsync()
    {
        var name = PromptDialog.Show(this, "Add device", "Device name:");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        await RunSharedMutationAsync(api => api.CreateDeviceAsync(new DevicePayload { Name = name.Trim() }));
    }

    private async Task RenameDeviceAsync()
    {
        if (_devicesList.SelectedItem is not DeviceDto device)
        {
            return;
        }

        var name = PromptDialog.Show(this, "Rename device", "Device name:", device.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        await RunSharedMutationAsync(api => api.UpdateDeviceAsync(device.Id, new DevicePayload { Name = name.Trim() }));
    }

    private async Task DeleteDeviceAsync()
    {
        if (_devicesList.SelectedItem is not DeviceDto device)
        {
            return;
        }

        if (MessageBox.Show(this, $"Delete device '{device.Name}'?", "Project Status",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }

        await RunSharedMutationAsync(async api =>
        {
            await api.DeleteDeviceAsync(device.Id);
            return device;
        });
    }

    private async Task RunSharedMutationAsync<T>(Func<ApiClient, Task<T>> action)
    {
        try
        {
            using var api = CreateApiFromField();
            await action(api);
            await ReloadSharedAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private ApiClient CreateApiFromField()
    {
        var address = AppSettings.NormalizeServerAddress(_serverText.Text);
        return new ApiClient(address);
    }

    private void SaveAndClose()
    {
        try
        {
            var server = AppSettings.NormalizeServerAddress(_serverText.Text);
            var device = _localDeviceCombo.Text.Trim();
            if (string.IsNullOrWhiteSpace(device))
            {
                throw new ArgumentException("This computer name is required.");
            }

            if (device.Length > 100)
            {
                throw new ArgumentException("Computer name must be at most 100 characters.");
            }

            _settings.ServerAddress = server;
            _settings.LocalDeviceName = device;
            _settings.AlwaysOnTop = _alwaysOnTopCheck.Checked;
            _settings.StartWithWindows = _autostartCheck.Checked;
            AppSettingsStore.Save(_settings);

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private void ShowError(string message)
    {
        MessageBox.Show(this, message, "Project Status", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}

internal sealed class FirstRunForm : Form
{
    private readonly AppSettings _settings;
    private readonly TextBox _serverText;
    private readonly TextBox _deviceText;

    public FirstRunForm(AppSettings settings)
    {
        _settings = settings;

        Text = "Project Status — First run";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(430, 190);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(14)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var intro = new Label
        {
            Text = "Two small settings are needed before Project Status can start.",
            AutoSize = true
        };
        layout.Controls.Add(intro, 0, 0);
        layout.SetColumnSpan(intro, 2);

        layout.Controls.Add(new Label { Text = "1. Server:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        _serverText = new TextBox { Dock = DockStyle.Fill, Text = settings.ServerAddress };
        layout.Controls.Add(_serverText, 1, 1);

        layout.Controls.Add(new Label { Text = "2. This computer:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        _deviceText = new TextBox { Dock = DockStyle.Fill, Text = settings.LocalDeviceName };
        layout.Controls.Add(_deviceText, 1, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft
        };
        var continueButton = new Button { Text = "Continue", AutoSize = true };
        continueButton.Click += (_, _) => Save();
        var cancelButton = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.Add(continueButton);
        buttons.Controls.Add(cancelButton);
        layout.Controls.Add(buttons, 0, 3);
        layout.SetColumnSpan(buttons, 2);

        Controls.Add(layout);
        AcceptButton = continueButton;
        CancelButton = cancelButton;
    }

    private void Save()
    {
        try
        {
            var server = AppSettings.NormalizeServerAddress(_serverText.Text);
            var device = _deviceText.Text.Trim();
            if (string.IsNullOrWhiteSpace(device))
            {
                throw new ArgumentException("Computer name is required.");
            }

            if (device.Length > 100)
            {
                throw new ArgumentException("Computer name must be at most 100 characters.");
            }

            _settings.ServerAddress = server;
            _settings.LocalDeviceName = device;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Project Status", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

internal static class PromptDialog
{
    public static string? Show(IWin32Window owner, string title, string prompt, string initialValue = "")
    {
        using var form = new Form
        {
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ClientSize = new Size(390, 125)
        };

        var label = new Label { Text = prompt, AutoSize = true, Location = new Point(12, 14) };
        var text = new TextBox
        {
            Text = initialValue,
            Location = new Point(12, 38),
            Width = 366,
            MaxLength = 100
        };
        text.SelectAll();

        var ok = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Location = new Point(222, 78),
            Width = 75
        };
        var cancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(303, 78),
            Width = 75
        };

        form.Controls.Add(label);
        form.Controls.Add(text);
        form.Controls.Add(ok);
        form.Controls.Add(cancel);
        form.AcceptButton = ok;
        form.CancelButton = cancel;

        return form.ShowDialog(owner) == DialogResult.OK ? text.Text.Trim() : null;
    }
}
