namespace ProjectStatus.Client;

internal sealed class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly TextBox _serverText;
    private readonly ComboBox _localDeviceCombo;
    private readonly CheckBox _alwaysOnTopCheck;
    private readonly CheckBox _autostartCheck;
    private readonly ComboBox _languageCombo;
    private readonly ListBox _projectsList;
    private readonly ListBox _statusesList;
    private readonly ListBox _devicesList;

    // Built by BuildAppearanceTab, which runs from the constructor.
    private TrackBar _opacityTrack = null!;
    private Label _opacityHint = null!;
    private Panel _backgroundPreview = null!;
    private Color _pendingBackground;
    private readonly Label _connectionLabel;

    public SettingsForm(AppSettings settings, StateSnapshot state)
    {
        _settings = settings;

        Text = Strings.AppTitleSettings;
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

        root.Controls.Add(new Label { Text = Strings.LabelServer, AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        _serverText = new TextBox { Dock = DockStyle.Fill, Text = settings.ServerAddress };
        root.Controls.Add(_serverText, 1, 0);

        root.Controls.Add(new Label { Text = Strings.LabelThisComputer, AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
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
        _alwaysOnTopCheck = new CheckBox { Text = Strings.OptionAlwaysOnTop, Checked = settings.AlwaysOnTop, AutoSize = true };
        _autostartCheck = new CheckBox { Text = Strings.OptionStartWithWindows, Checked = settings.StartWithWindows, AutoSize = true };
        options.Controls.Add(_alwaysOnTopCheck);
        options.Controls.Add(_autostartCheck);

        options.Controls.Add(new Label
        {
            Text = Strings.LabelLanguage,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Padding = new Padding(12, 7, 0, 0)
        });
        _languageCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
        _languageCombo.Items.AddRange(
            new object[]
            {
                new LanguageChoice(LanguagePreference.Auto, Strings.LanguageAuto),
                new LanguageChoice(LanguagePreference.Russian, Strings.LanguageRussian),
                new LanguageChoice(LanguagePreference.English, Strings.LanguageEnglish)
            });
        _languageCombo.DisplayMember = nameof(LanguageChoice.Label);
        _languageCombo.SelectedItem = _languageCombo.Items
            .OfType<LanguageChoice>()
            .First(choice => choice.Preference == Localization.FromStoredValue(settings.Language));
        options.Controls.Add(_languageCombo);
        root.Controls.Add(options, 1, 2);

        var connectionPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight
        };
        var testButton = new Button { Text = Strings.ButtonTestConnection, AutoSize = true };
        testButton.Click += async (_, _) => await TestConnectionAsync();
        _connectionLabel = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(4, 7, 0, 0) };
        connectionPanel.Controls.Add(testButton);
        connectionPanel.Controls.Add(_connectionLabel);
        root.Controls.Add(connectionPanel, 1, 3);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        _projectsList = new ListBox
        {
            Dock = DockStyle.Fill,
            // Without this the list shows the type name of the item (ProjectStatus.Client.ProjectDto).
            DisplayMember = ProjectDto.DisplayMemberProperty
        };
        _statusesList = new ListBox
        {
            Dock = DockStyle.Fill,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = Math.Max(18, Font.Height + 6)
        };
        _statusesList.DrawItem += DrawStatusItem;
        _devicesList = new ListBox { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildProjectsTab());
        tabs.TabPages.Add(BuildStatusesTab());
        tabs.TabPages.Add(BuildDevicesTab());
        tabs.TabPages.Add(BuildAppearanceTab());
        root.Controls.Add(tabs, 0, 4);
        root.SetColumnSpan(tabs, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft
        };
        var saveButton = new Button { Text = Strings.ButtonSave, DialogResult = DialogResult.None, AutoSize = true };
        saveButton.Click += (_, _) => SaveAndClose();
        var cancelButton = new Button { Text = Strings.ButtonCancel, DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.Add(saveButton);
        buttons.Controls.Add(cancelButton);
        root.Controls.Add(buttons, 0, 5);
        root.SetColumnSpan(buttons, 2);

        Controls.Add(root);
        AcceptButton = saveButton;
        CancelButton = cancelButton;

        Shown += async (_, _) => await ReloadSharedAsync(showError: false);
    }

    private TabPage BuildAppearanceTab()
    {
        var page = new TabPage(Strings.TabAppearance);
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 5,
            Padding = new Padding(10)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < 4; row++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(new Label { Text = Strings.LabelCompactOpacity, AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);

        _opacityTrack = new TrackBar
        {
            Dock = DockStyle.Fill,
            Minimum = AppearanceSettings.MinimumOpacityPercent,
            Maximum = AppearanceSettings.MaximumOpacityPercent,
            TickFrequency = 5,
            SmallChange = 5,
            LargeChange = 10,
            Value = AppearanceSettings.NormalizeOpacityPercent(_settings.CompactOpacity)
        };
        _opacityTrack.ValueChanged += (_, _) => UpdateOpacityHint();
        layout.Controls.Add(_opacityTrack, 1, 0);

        _opacityHint = new Label { AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = SystemColors.GrayText };
        layout.Controls.Add(_opacityHint, 1, 1);

        layout.Controls.Add(new Label { Text = Strings.LabelBackground, AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);

        var backgroundPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        _backgroundPreview = new Panel { Size = new Size(30, 22), BorderStyle = BorderStyle.FixedSingle };
        var chooseButton = new Button { Text = Strings.ButtonChooseColor, AutoSize = true };
        chooseButton.Click += (_, _) => ChooseBackgroundColor();
        var defaultButton = new Button { Text = Strings.ButtonUseDefault, AutoSize = true };
        defaultButton.Click += (_, _) => SetPendingBackground(Color.Empty);
        backgroundPanel.Controls.Add(_backgroundPreview);
        backgroundPanel.Controls.Add(chooseButton);
        backgroundPanel.Controls.Add(defaultButton);
        layout.Controls.Add(backgroundPanel, 1, 2);

        var resetButton = new Button { Text = Strings.ButtonResetAppearance, AutoSize = true };
        resetButton.Click += (_, _) => ResetAppearance();
        layout.Controls.Add(resetButton, 1, 3);

        SetPendingBackground(AppearanceSettings.ParseBackgroundColor(_settings.BackgroundColor));
        UpdateOpacityHint();

        page.Controls.Add(layout);
        return page;
    }

    private void UpdateOpacityHint()
    {
        _opacityHint.Text = Strings.CompactOpacityHintFormat(_opacityTrack.Value);
    }

    private void SetPendingBackground(Color color)
    {
        _pendingBackground = color;
        _backgroundPreview.BackColor = color.IsEmpty ? SystemColors.Control : color;
    }

    private void ChooseBackgroundColor()
    {
        using var dialog = new ColorDialog
        {
            FullOpen = true,
            Color = _pendingBackground.IsEmpty ? SystemColors.Control : _pendingBackground
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            SetPendingBackground(dialog.Color);
        }
    }

    // Values are written when Save is pressed, like the rest of the settings.
    private void ResetAppearance()
    {
        _opacityTrack.Value = AppearanceSettings.DefaultOpacityPercent;
        SetPendingBackground(Color.Empty);
    }

    private TabPage BuildProjectsTab()
    {
        var page = new TabPage(Strings.TabProjects);
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(6)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(_projectsList, 0, 0);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        buttons.Controls.Add(ActionButton(Strings.ButtonAdd, async () => await AddProjectAsync()));
        buttons.Controls.Add(ActionButton(Strings.ButtonRename, async () => await RenameProjectAsync()));
        buttons.Controls.Add(ActionButton(Strings.ButtonDelete, async () => await DeleteProjectAsync()));
        layout.Controls.Add(buttons, 0, 1);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildStatusesTab()
    {
        var page = new TabPage(Strings.TabStatuses);
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
        buttons.Controls.Add(ActionButton(Strings.ButtonAdd, async () => await AddStatusAsync()));
        buttons.Controls.Add(ActionButton(Strings.ButtonRename, async () => await RenameStatusAsync()));
        buttons.Controls.Add(ActionButton(Strings.ButtonColor, async () => await ChangeStatusColorAsync()));
        buttons.Controls.Add(ActionButton(Strings.ButtonDelete, async () => await DeleteStatusAsync()));
        layout.Controls.Add(buttons, 0, 1);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildDevicesTab()
    {
        var page = new TabPage(Strings.TabDevices);
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
        buttons.Controls.Add(ActionButton(Strings.ButtonAdd, async () => await AddDeviceAsync()));
        buttons.Controls.Add(ActionButton(Strings.ButtonRename, async () => await RenameDeviceAsync()));
        buttons.Controls.Add(ActionButton(Strings.ButtonDelete, async () => await DeleteDeviceAsync()));
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
            _connectionLabel.Text = Strings.TestOk;
            _connectionLabel.ForeColor = Color.DarkGreen;
        }
        catch (Exception ex)
        {
            _connectionLabel.Text = Strings.TestFailed;
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

            _projectsList.Items.Clear();
            foreach (var project in state.Projects)
            {
                _projectsList.Items.Add(project);
            }

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

    // Project lifecycle moved here from the main window: the grid no longer creates, renames or
    // deletes projects. Every operation re-reads the shared state afterwards, so a rejected call
    // never leaves the dialog showing something the server did not accept.
    private async Task AddProjectAsync()
    {
        var name = PromptDialog.Show(this, Strings.PromptAddProjectTitle, Strings.PromptProjectName);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        await RunSharedMutationAsync(api => api.CreateProjectAsync(new ProjectPayload
        {
            Name = name.Trim(),
            StatusId = FindPausedStatusId(),
            DeviceId = null,
            Note = string.Empty
        }));
    }

    private async Task RenameProjectAsync()
    {
        if (_projectsList.SelectedItem is not ProjectDto selected)
        {
            return;
        }

        var name = PromptDialog.Show(this, Strings.PromptRenameProjectTitle, Strings.PromptProjectName, selected.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        await RunSharedMutationAsync(async api =>
        {
            // The list in this dialog can be seconds old and another client may have changed the
            // project since it was read. Only the name is taken from the dialog; status, device and
            // note are taken from the freshly read state, so a rename cannot put stale values back.
            var state = await api.GetStateAsync();
            var current = state.Projects.FirstOrDefault(project => project.Id == selected.Id);
            if (current is null)
            {
                throw new ApiException(Strings.ErrorProjectGone);
            }

            return await api.UpdateProjectAsync(current.Id, new ProjectPayload
            {
                Name = name.Trim(),
                StatusId = current.StatusId,
                DeviceId = current.DeviceId,
                Note = current.Note
            });
        });
    }

    private async Task DeleteProjectAsync()
    {
        if (_projectsList.SelectedItem is not ProjectDto project)
        {
            return;
        }

        if (MessageBox.Show(this, Strings.ConfirmDeleteProjectFormat(project.Name), Strings.AppTitle,
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }

        await RunSharedMutationAsync(async api =>
        {
            await api.DeleteProjectAsync(project.Id);
            return project;
        });
    }

    // A new project starts in the "Paused" stage when the server has one, which keeps the previous
    // main-window behaviour after the button moved here.
    private int? FindPausedStatusId()
    {
        return _statusesList.Items
            .OfType<StatusDto>()
            .FirstOrDefault(status =>
                string.Equals(status.Name, "Paused", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(status.Name, "На паузе", StringComparison.OrdinalIgnoreCase))
            ?.Id;
    }

    private async Task AddStatusAsync()
    {
        var name = PromptDialog.Show(this, Strings.PromptAddStatusTitle, Strings.PromptStatusName);
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

        var name = PromptDialog.Show(this, Strings.PromptRenameStatusTitle, Strings.PromptStatusName, status.Name);
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

        if (MessageBox.Show(this, Strings.ConfirmDeleteStatusFormat(status.Name), Strings.AppTitle,
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
        var name = PromptDialog.Show(this, Strings.PromptAddDeviceTitle, Strings.PromptDeviceName);
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

        var name = PromptDialog.Show(this, Strings.PromptRenameDeviceTitle, Strings.PromptDeviceName, device.Name);
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

        if (MessageBox.Show(this, Strings.ConfirmDeleteDeviceFormat(device.Name), Strings.AppTitle,
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
                throw new ArgumentException(Strings.ErrorComputerNameRequired);
            }

            if (device.Length > 100)
            {
                throw new ArgumentException(Strings.ErrorComputerNameTooLong);
            }

            _settings.ServerAddress = server;
            _settings.LocalDeviceName = device;
            _settings.AlwaysOnTop = _alwaysOnTopCheck.Checked;
            _settings.StartWithWindows = _autostartCheck.Checked;

            if (_languageCombo.SelectedItem is LanguageChoice language)
            {
                _settings.Language = Localization.ToStoredValue(language.Preference);
            }
            _settings.CompactOpacity = AppearanceSettings.NormalizeOpacityPercent(_opacityTrack.Value);
            _settings.BackgroundColor = _pendingBackground.IsEmpty
                ? AppearanceSettings.DefaultBackgroundColor
                : AppearanceSettings.ToHex(_pendingBackground);
            AppSettingsStore.Save(_settings);

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    // Statuses are shown with a square filled with the color assigned to them, so the list shows
    // the mapping without opening the color dialog. Items stay StatusDto: the buttons above rely on
    // SelectedItem being the status itself.
    private static void DrawStatusItem(object? sender, DrawItemEventArgs e)
    {
        if (sender is not ListBox list || e.Index < 0 || e.Index >= list.Items.Count)
        {
            return;
        }

        if (list.Items[e.Index] is not StatusDto status)
        {
            return;
        }

        var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        using (var background = new SolidBrush(selected ? SystemColors.Highlight : list.BackColor))
        {
            e.Graphics.FillRectangle(background, e.Bounds);
        }

        var color = ParseStatusColor(status.Color);
        var size = Math.Max(8, Math.Min(12, e.Bounds.Height - 6));
        var marker = new Rectangle(
            e.Bounds.Left + 4,
            e.Bounds.Top + ((e.Bounds.Height - size) / 2),
            size,
            size);

        using (var fill = new SolidBrush(color))
        using (var border = new Pen(ControlPaint.Dark(color)))
        {
            e.Graphics.FillRectangle(fill, marker);
            e.Graphics.DrawRectangle(border, marker);
        }

        var text = new Rectangle(
            marker.Right + 6,
            e.Bounds.Top,
            Math.Max(0, e.Bounds.Right - marker.Right - 8),
            e.Bounds.Height);

        TextRenderer.DrawText(
            e.Graphics,
            status.Name,
            list.Font,
            text,
            selected ? SystemColors.HighlightText : list.ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        e.DrawFocusRectangle();
    }

    private static Color ParseStatusColor(string value)
    {
        try
        {
            var color = ColorTranslator.FromHtml(value);
            return color.IsEmpty ? Color.SlateGray : color;
        }
        catch
        {
            // Server validates colors; malformed legacy data should not break the list.
            return Color.SlateGray;
        }
    }

    private static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    // One entry of the language selector. The label is what the user sees, the preference is stored.
    private sealed record LanguageChoice(LanguagePreference Preference, string Label);

    private void ShowError(string message)
    {
        MessageBox.Show(this, message, Strings.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
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

        Text = Strings.AppTitleFirstRun;
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
            Text = Strings.FirstRunIntro,
            AutoSize = true
        };
        layout.Controls.Add(intro, 0, 0);
        layout.SetColumnSpan(intro, 2);

        layout.Controls.Add(new Label { Text = Strings.FirstRunServerLabel, AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        _serverText = new TextBox { Dock = DockStyle.Fill, Text = settings.ServerAddress };
        layout.Controls.Add(_serverText, 1, 1);

        layout.Controls.Add(new Label { Text = Strings.FirstRunComputerLabel, AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        _deviceText = new TextBox { Dock = DockStyle.Fill, Text = settings.LocalDeviceName };
        layout.Controls.Add(_deviceText, 1, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft
        };
        var continueButton = new Button { Text = Strings.ButtonContinue, AutoSize = true };
        continueButton.Click += (_, _) => Save();
        var cancelButton = new Button { Text = Strings.ButtonCancel, DialogResult = DialogResult.Cancel, AutoSize = true };
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
                throw new ArgumentException(Strings.ErrorComputerNameRequired);
            }

            if (device.Length > 100)
            {
                throw new ArgumentException(Strings.ErrorComputerNameTooLong);
            }

            _settings.ServerAddress = server;
            _settings.LocalDeviceName = device;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Strings.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            Text = Strings.ButtonOk,
            DialogResult = DialogResult.OK,
            Location = new Point(222, 78),
            Width = 75
        };
        var cancel = new Button
        {
            Text = Strings.ButtonCancel,
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
