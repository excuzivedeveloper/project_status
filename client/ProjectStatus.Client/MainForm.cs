using System.Globalization;
using System.Text;

namespace ProjectStatus.Client;

internal sealed class MainForm : Form
{
    private const int PollIntervalMs = 5000;
    private const string NameColumn = "name";
    private const string StatusColumn = "status";
    private const string DeviceColumn = "device";
    private const string NoteColumn = "note";
    private const string UpdatedColumn = "updated";
    private static readonly Color PinActiveBackColor = Color.FromArgb(0, 120, 215);

    private readonly AppSettings _settings;
    private readonly ApiClient _api;
    private readonly DataGridView _grid;
    private readonly ToolStripButton _pinButton;
    private readonly ToolStripStatusLabel _syncLabel;
    private readonly ToolStripStatusLabel _deviceLabel;
    private readonly System.Windows.Forms.Timer _pollTimer;
    private readonly SemaphoreSlim _apiGate = new(1, 1);

    private StateSnapshot _state = new();
    private string _stateSignature = string.Empty;
    private bool _binding;
    private bool _syncingPin;
    private bool _allowExit;

    public event EventHandler? AlwaysOnTopChanged;

    public MainForm(AppSettings settings, ApiClient api)
    {
        _settings = settings;
        _api = api;

        Text = "Project Status";
        MinimumSize = new Size(620, 260);
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = settings.AlwaysOnTop;
        RestoreWindowBounds();

        var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        if (icon is not null)
        {
            Icon = icon;
        }

        var toolStrip = new ToolStrip
        {
            GripStyle = ToolStripGripStyle.Hidden,
            Dock = DockStyle.Top,
            Padding = new Padding(4, 2, 4, 2)
        };

        // The stock checked-state rendering is too subtle to show that Pin is on, so a checked
        // toolbar button is filled with an accent color instead.
        toolStrip.Renderer = new PinCheckedRenderer();

        var addButton = new ToolStripButton("+ Project");
        addButton.Click += async (_, _) => await AddProjectAsync();

        var deleteButton = new ToolStripButton("Delete");
        deleteButton.Click += async (_, _) => await DeleteSelectedProjectAsync();

        var settingsButton = new ToolStripButton("Settings");
        settingsButton.Click += async (_, _) => await ShowSettingsDialogAsync();

        _pinButton = new ToolStripButton("Pin")
        {
            CheckOnClick = true,
            Checked = settings.AlwaysOnTop,
            ToolTipText = "Keep the window above other windows"
        };
        _pinButton.CheckedChanged += (_, _) =>
        {
            if (!_syncingPin)
            {
                SetAlwaysOnTop(_pinButton.Checked);
            }
        };

        toolStrip.Items.Add(addButton);
        toolStrip.Items.Add(deleteButton);
        toolStrip.Items.Add(new ToolStripSeparator());
        toolStrip.Items.Add(settingsButton);
        toolStrip.Items.Add(_pinButton);

        // Toolbar actions run against the row the user was working in, so a pending text edit is
        // committed on mouse down and the caret is parked on that same row.
        foreach (var button in new[] { addButton, deleteButton, settingsButton })
        {
            button.MouseDown += (_, _) => CommitPendingGridEdit();
        }

        UpdatePinVisual(settings.AlwaysOnTop);

        _grid = BuildGrid();
        _grid.Dock = DockStyle.Fill;

        var statusStrip = new StatusStrip();
        _syncLabel = new ToolStripStatusLabel("No connection");
        var spring = new ToolStripStatusLabel { Spring = true };
        _deviceLabel = new ToolStripStatusLabel($"This PC: {settings.LocalDeviceName}");
        statusStrip.Items.Add(_syncLabel);
        statusStrip.Items.Add(spring);
        statusStrip.Items.Add(_deviceLabel);

        Controls.Add(_grid);
        Controls.Add(toolStrip);
        Controls.Add(statusStrip);

        _pollTimer = new System.Windows.Forms.Timer { Interval = PollIntervalMs };
        _pollTimer.Tick += async (_, _) => await RefreshStateAsync(force: false);

        FormClosing += OnFormClosing;
        Deactivate += (_, _) => CommitPendingGridEdit();
        ResizeEnd += (_, _) => CaptureWindowSettings();
        Move += (_, _) =>
        {
            if (WindowState == FormWindowState.Normal)
            {
                CaptureWindowSettings();
            }
        };
    }

    public void StartPolling()
    {
        _pollTimer.Start();
        _ = RefreshStateAsync(force: true);
    }

    public void ShowFromTray()
    {
        if (!Visible)
        {
            ShowInTaskbar = true;
            Show();
        }

        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }

        Activate();
        BringToFront();

        SetMainWindowVisible(true);
    }

    public void HideToTray()
    {
        CaptureWindowSettings();
        Hide();
        ShowInTaskbar = false;

        SetMainWindowVisible(false);
    }

    // The --startup launch restores this, so the window comes back the way the user left it: open
    // when it was open, hidden in the tray when it was hidden.
    private void SetMainWindowVisible(bool visible)
    {
        if (_settings.MainWindowVisible == visible)
        {
            return;
        }

        _settings.MainWindowVisible = visible;
        AppSettingsStore.Save(_settings);
    }

    public void SetAlwaysOnTop(bool value)
    {
        TopMost = value;
        _settings.AlwaysOnTop = value;
        AppSettingsStore.Save(_settings);

        _syncingPin = true;
        try
        {
            _pinButton.Checked = value;
        }
        finally
        {
            _syncingPin = false;
        }

        UpdatePinVisual(value);
        AlwaysOnTopChanged?.Invoke(this, EventArgs.Empty);
    }

    public void CaptureWindowSettings()
    {
        var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        _settings.WindowX = bounds.X;
        _settings.WindowY = bounds.Y;
        _settings.WindowWidth = Math.Max(MinimumSize.Width, bounds.Width);
        _settings.WindowHeight = Math.Max(MinimumSize.Height, bounds.Height);
        _settings.AlwaysOnTop = TopMost;
        _settings.MainWindowVisible = Visible;
        AppSettingsStore.Save(_settings);
    }

    public void AllowExitAndClose()
    {
        _allowExit = true;
        _pollTimer.Stop();
        Close();
    }

    public async Task ShowSettingsDialogAsync()
    {
        CommitPendingGridEdit();

        using var dialog = new SettingsForm(_settings, _state);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            _settings.ServerAddress = AppSettings.NormalizeServerAddress(_settings.ServerAddress);
            _api.SetServerAddress(_settings.ServerAddress);
            AutostartManager.Apply(_settings.StartWithWindows);
            _deviceLabel.Text = $"This PC: {_settings.LocalDeviceName}";
            SetAlwaysOnTop(_settings.AlwaysOnTop);
            _stateSignature = string.Empty;

            // Settings edits statuses and devices on the server. The main form has to show them when
            // the dialog is gone, so wait for the refreshed snapshot instead of starting it and hoping
            // the poll timer picks it up before the user looks at the grid again.
            await RefreshStateAsync(force: true);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    // Commits an in-progress grid edit and takes the caret off the edited cell.
    // DataGridView.EndEdit() is not enough while EditMode is EditOnEnter: it commits the value but
    // starts a new edit session on the same cell, so the caret stays and the cell keeps looking
    // editable. Assigning the current cell is what actually ends the session; the read-only Updated
    // cell is the target because a read-only cell cannot start another session, while its row stays
    // current so row-oriented actions (Delete) still see the project the user was working with.
    internal void CommitPendingGridEdit()
    {
        if (_grid.IsDisposed || !_grid.IsCurrentCellInEditMode)
        {
            return;
        }

        var row = _grid.CurrentCell?.OwningRow;
        if (row is null)
        {
            _grid.EndEdit();
            return;
        }

        _grid.CurrentCell = row.Cells[UpdatedColumn];
    }

    private bool IsTextColumn(int columnIndex)
    {
        return columnIndex >= 0 &&
               columnIndex < _grid.Columns.Count &&
               _grid.Columns[columnIndex].Name is NameColumn or NoteColumn;
    }

    private DataGridView BuildGrid()
    {
        var grid = new ProjectGrid(this)
        {
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AutoGenerateColumns = false,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.None,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
            EditMode = DataGridViewEditMode.EditOnEnter,
            MultiSelect = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.CellSelect
        };

        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = NameColumn,
            HeaderText = "Project",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 24,
            MaxInputLength = 100
        });

        grid.Columns.Add(new DataGridViewComboBoxColumn
        {
            Name = StatusColumn,
            HeaderText = "Status",
            Width = 120,
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
            FlatStyle = FlatStyle.Flat
        });

        grid.Columns.Add(new DataGridViewComboBoxColumn
        {
            Name = DeviceColumn,
            HeaderText = "Device",
            Width = 100,
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
            FlatStyle = FlatStyle.Flat
        });

        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = NoteColumn,
            HeaderText = "Note",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 38,
            MaxInputLength = 200
        });

        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = UpdatedColumn,
            HeaderText = "Updated",
            Width = 115,
            ReadOnly = true
        });

        grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (grid.IsCurrentCellDirty && grid.CurrentCell is DataGridViewComboBoxCell)
            {
                grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };

        grid.CellValueChanged += async (_, e) =>
        {
            if (_binding || e.RowIndex < 0)
            {
                return;
            }

            var columnName = grid.Columns[e.ColumnIndex].Name;
            if (columnName is StatusColumn or DeviceColumn)
            {
                await SaveRowAsync(e.RowIndex);
            }
        };

        grid.CellEndEdit += async (_, e) =>
        {
            if (_binding || e.RowIndex < 0)
            {
                return;
            }

            var columnName = grid.Columns[e.ColumnIndex].Name;
            if (columnName is NameColumn or NoteColumn)
            {
                await SaveRowAsync(e.RowIndex);
            }
        };

        grid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 || grid.Columns[e.ColumnIndex].Name != StatusColumn)
            {
                return;
            }

            var statusId = ParseNullableId(grid.Rows[e.RowIndex].Cells[StatusColumn].Value);
            if (statusId is not int id)
            {
                return;
            }

            var status = _state.Statuses.FirstOrDefault(item => item.Id == id);
            if (status is null || e.CellStyle is not DataGridViewCellStyle style)
            {
                return;
            }

            try
            {
                var color = ColorTranslator.FromHtml(status.Color);
                var textColor = GetContrastingTextColor(color);
                style.BackColor = color;
                style.ForeColor = textColor;
                style.SelectionBackColor = color;
                style.SelectionForeColor = textColor;
            }
            catch
            {
                // Server validates colors; malformed legacy data should not break the UI.
            }
        };

        grid.DataError += (_, e) => e.ThrowException = false;

        // Leaving the grid (toolbar, taskbar, another window) commits the pending edit as well.
        grid.Leave += (_, _) => CommitPendingGridEdit();

        // A click on the empty area below the rows ends the edit like Enter does.
        grid.MouseDown += (_, e) =>
        {
            if (grid.HitTest(e.X, e.Y).Type == DataGridViewHitTestType.None)
            {
                CommitPendingGridEdit();
            }
        };

        return grid;
    }

    private async Task RefreshStateAsync(bool force)
    {
        if (force)
        {
            // A forced refresh is triggered by the app itself (startup, toolbar action, after
            // Settings), so it finishes the pending edit first: an active editing control would
            // otherwise cause the freshly fetched snapshot to be dropped.
            CommitPendingGridEdit();
        }
        else if (_grid.IsCurrentCellInEditMode)
        {
            // A background poll must never rebind the grid while the user is really editing.
            return;
        }

        var acquired = force ? await WaitForGateAsync() : _apiGate.Wait(0);
        if (!acquired)
        {
            return;
        }

        try
        {
            var state = await _api.GetStateAsync();
            state = await EnsureLocalDeviceExistsAsync(state);
            SetSyncOk();

            // The user may have started editing while the HTTP request was in flight. A poll gives up
            // and lets the next tick apply the snapshot; a forced refresh ends that edit instead,
            // because its caller is waiting for the grid to show the current server state.
            if (!force && _grid.IsCurrentCellInEditMode)
            {
                return;
            }

            if (force)
            {
                CommitPendingGridEdit();
            }

            var signature = ComputeStateSignature(state);
            if (signature == _stateSignature)
            {
                return;
            }

            _state = state;
            _stateSignature = signature;
            BindState();
        }
        catch
        {
            SetSyncOffline();
        }
        finally
        {
            _apiGate.Release();
        }
    }

    private async Task<bool> WaitForGateAsync()
    {
        await _apiGate.WaitAsync();
        return true;
    }

    private async Task<StateSnapshot> EnsureLocalDeviceExistsAsync(StateSnapshot state)
    {
        var localName = _settings.LocalDeviceName.Trim();
        if (string.IsNullOrWhiteSpace(localName) ||
            state.Devices.Any(device => string.Equals(device.Name, localName, StringComparison.OrdinalIgnoreCase)))
        {
            return state;
        }

        try
        {
            await _api.CreateDeviceAsync(new DevicePayload { Name = localName });
        }
        catch (ApiException)
        {
            // Another client may have created the same device concurrently.
        }

        return await _api.GetStateAsync();
    }

    private void BindState()
    {
        var selectedProjectId = SelectedProject()?.Id;

        _binding = true;
        try
        {
            var statusColumn = (DataGridViewComboBoxColumn)_grid.Columns[StatusColumn];
            statusColumn.DataSource = BuildChoices(_state.Statuses.Select(status => (status.Id, status.Name)));
            statusColumn.DisplayMember = nameof(ChoiceItem.Name);
            statusColumn.ValueMember = nameof(ChoiceItem.Id);
            statusColumn.ValueType = typeof(string);

            var deviceColumn = (DataGridViewComboBoxColumn)_grid.Columns[DeviceColumn];
            deviceColumn.DataSource = BuildChoices(_state.Devices.Select(device => (device.Id, device.Name)));
            deviceColumn.DisplayMember = nameof(ChoiceItem.Name);
            deviceColumn.ValueMember = nameof(ChoiceItem.Id);
            deviceColumn.ValueType = typeof(string);

            _grid.Rows.Clear();
            foreach (var project in _state.Projects)
            {
                var rowIndex = _grid.Rows.Add(
                    project.Name,
                    project.StatusId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                    project.DeviceId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                    project.Note,
                    FormatUpdated(project.UpdatedAt));
                _grid.Rows[rowIndex].Tag = project;
            }

            if (selectedProjectId is int id)
            {
                foreach (DataGridViewRow row in _grid.Rows)
                {
                    if (row.Tag is ProjectDto project && project.Id == id)
                    {
                        row.Cells[0].Selected = true;
                        break;
                    }
                }
            }

            // Rebuilding the rows makes the grid pick a current cell again and, in EditOnEnter mode,
            // start editing it. Park the caret: a session left behind here would keep the background
            // polling from ever applying the next snapshot. Saves are suppressed while binding.
            CommitPendingGridEdit();
        }
        finally
        {
            _binding = false;
        }
    }

    private static List<ChoiceItem> BuildChoices(IEnumerable<(int Id, string Name)> source)
    {
        var result = new List<ChoiceItem> { new(string.Empty, "—") };
        result.AddRange(source.Select(item =>
            new ChoiceItem(item.Id.ToString(CultureInfo.InvariantCulture), item.Name)));
        return result;
    }

    private async Task AddProjectAsync()
    {
        var name = PromptDialog.Show(this, "New project", "Project name:");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var paused = _state.Statuses.FirstOrDefault(status =>
            string.Equals(status.Name, "Paused", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status.Name, "На паузе", StringComparison.OrdinalIgnoreCase));

        await RunMutationAsync(async () =>
        {
            await _api.CreateProjectAsync(new ProjectPayload
            {
                Name = name.Trim(),
                StatusId = paused?.Id,
                DeviceId = null,
                Note = string.Empty
            });
        });
    }

    private async Task DeleteSelectedProjectAsync()
    {
        var project = SelectedProject();
        if (project is null)
        {
            return;
        }

        var result = MessageBox.Show(
            this,
            $"Delete project '{project.Name}'?",
            "Project Status",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (result != DialogResult.Yes)
        {
            return;
        }

        await RunMutationAsync(() => _api.DeleteProjectAsync(project.Id));
    }

    private ProjectDto? SelectedProject()
    {
        return _grid.CurrentCell?.OwningRow?.Tag as ProjectDto;
    }

    private async Task SaveRowAsync(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _grid.Rows.Count)
        {
            return;
        }

        var row = _grid.Rows[rowIndex];
        if (row.Tag is not ProjectDto current)
        {
            return;
        }

        var name = Convert.ToString(row.Cells[NameColumn].Value)?.Trim() ?? string.Empty;
        var note = Convert.ToString(row.Cells[NoteColumn].Value)?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name))
        {
            ShowError("Project name cannot be blank.");
            await RefreshStateAsync(force: true);
            return;
        }

        if (note.Length > 200 || note.Contains('\r') || note.Contains('\n'))
        {
            ShowError("Note must be one line and at most 200 characters.");
            await RefreshStateAsync(force: true);
            return;
        }

        var statusId = ParseNullableId(row.Cells[StatusColumn].Value);
        var deviceId = ParseNullableId(row.Cells[DeviceColumn].Value);

        // Ending an edit session also happens when nothing was typed (leaving the cell, toolbar
        // clicks, refreshes), and CellEndEdit fires for those too. Skip the request when the row
        // still holds the values the server gave us: writing them back would only move Updated.
        if (name == current.Name &&
            note == current.Note &&
            statusId == current.StatusId &&
            deviceId == current.DeviceId)
        {
            return;
        }

        await _apiGate.WaitAsync();
        try
        {
            var updated = await _api.UpdateProjectAsync(current.Id, new ProjectPayload
            {
                Name = name,
                StatusId = statusId,
                DeviceId = deviceId,
                Note = note
            });

            row.Tag = updated;
            row.Cells[UpdatedColumn].Value = FormatUpdated(updated.UpdatedAt);
            SetSyncOk();
            _stateSignature = string.Empty;
        }
        catch (Exception ex)
        {
            SetSyncOfflineIfNetwork(ex);
            ShowError(ex.Message);
        }
        finally
        {
            _apiGate.Release();
        }

        await RefreshStateAsync(force: true);
    }

    private async Task RunMutationAsync(Func<Task> action)
    {
        await _apiGate.WaitAsync();
        try
        {
            await action();
            SetSyncOk();
            _stateSignature = string.Empty;
        }
        catch (Exception ex)
        {
            SetSyncOfflineIfNetwork(ex);
            ShowError(ex.Message);
            return;
        }
        finally
        {
            _apiGate.Release();
        }

        await RefreshStateAsync(force: true);
    }

    private static int? ParseNullableId(object? value)
    {
        var text = Convert.ToString(value, CultureInfo.InvariantCulture);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            ? id
            : null;
    }

    private void SetSyncOk()
    {
        _syncLabel.Text = "Sync: OK";
        _syncLabel.ForeColor = Color.DarkGreen;
    }

    private void SetSyncOffline()
    {
        _syncLabel.Text = "No connection";
        _syncLabel.ForeColor = Color.Firebrick;
    }

    private void SetSyncOfflineIfNetwork(Exception ex)
    {
        if (ex is HttpRequestException or TaskCanceledException)
        {
            SetSyncOffline();
        }
    }

    private static string FormatUpdated(string value)
    {
        if (!DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed))
        {
            return value;
        }

        return parsed.ToLocalTime().ToString("dd MMM HH:mm", CultureInfo.CurrentCulture);
    }

    private static string ComputeStateSignature(StateSnapshot state)
    {
        var builder = new StringBuilder();
        foreach (var status in state.Statuses.OrderBy(item => item.Id))
        {
            builder.Append("S:").Append(status.Id).Append(':').Append(status.Name)
                .Append(':').Append(status.Color).Append('|');
        }

        foreach (var device in state.Devices.OrderBy(item => item.Id))
        {
            builder.Append("D:").Append(device.Id).Append(':').Append(device.Name).Append('|');
        }

        foreach (var project in state.Projects.OrderBy(item => item.Id))
        {
            builder.Append("P:").Append(project.Id).Append(':').Append(project.Name).Append(':')
                .Append(project.StatusId).Append(':').Append(project.DeviceId).Append(':')
                .Append(project.Note).Append(':').Append(project.UpdatedAt).Append('|');
        }

        return builder.ToString();
    }

    private static Color GetContrastingTextColor(Color background)
    {
        var luminance = (0.299 * background.R) + (0.587 * background.G) + (0.114 * background.B);
        return luminance > 150 ? Color.Black : Color.White;
    }

    // Pin is toggled from the toolbar, the tray and Settings, and every one of them goes through
    // SetAlwaysOnTop, so the button derives its look from the resulting window state in one place.
    private void UpdatePinVisual(bool active)
    {
        _pinButton.BackColor = active ? PinActiveBackColor : Color.Empty;
        _pinButton.ForeColor = active ? Color.White : Color.Empty;
        _pinButton.ToolTipText = active
            ? "Always on top is on"
            : "Keep the window above other windows";
    }

    private void RestoreWindowBounds()
    {
        var width = Math.Max(MinimumSize.Width, _settings.WindowWidth);
        var height = Math.Max(MinimumSize.Height, _settings.WindowHeight);

        if (_settings.WindowX is int x && _settings.WindowY is int y)
        {
            var desired = new Rectangle(x, y, width, height);
            if (Screen.AllScreens.Any(screen => screen.WorkingArea.IntersectsWith(desired)))
            {
                Bounds = desired;
                return;
            }
        }

        var working = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        width = Math.Min(width, working.Width);
        height = Math.Min(height, working.Height);
        Bounds = new Rectangle(
            working.Right - width - 24,
            working.Top + 24,
            width,
            height);
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_allowExit)
        {
            CaptureWindowSettings();
            return;
        }

        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideToTray();
        }
    }

    private void ShowError(string message)
    {
        MessageBox.Show(this, message, "Project Status", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private sealed record ChoiceItem(string Id, string Name);

    private sealed class PinCheckedRenderer : ToolStripProfessionalRenderer
    {
        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.Item is ToolStripButton { Checked: true } button && button.BackColor != Color.Empty)
            {
                using var brush = new SolidBrush(button.BackColor);
                e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
                return;
            }

            base.OnRenderButtonBackground(e);
        }
    }

    // Enter is handled here because ProcessDialogKey is the point where the grid sees the key while
    // an editing control owns the keyboard focus: the editing TextBox raises no KeyDown for it, so a
    // handler attached to that control never runs. Only a plain Enter on a text cell is taken over;
    // every other key keeps the stock DataGridView behaviour.
    private sealed class ProjectGrid(MainForm owner) : DataGridView
    {
        protected override bool ProcessDialogKey(Keys keyData)
        {
            if ((keyData & Keys.KeyCode) == Keys.Enter &&
                (keyData & Keys.Modifiers) == Keys.None &&
                IsCurrentCellInEditMode &&
                CurrentCell is not null &&
                owner.IsTextColumn(CurrentCell.ColumnIndex))
            {
                owner.CommitPendingGridEdit();
                return true;
            }

            return base.ProcessDialogKey(keyData);
        }
    }
}
