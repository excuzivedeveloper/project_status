using System.Globalization;
using System.Runtime.InteropServices;
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
    private const string PinButtonName = "PinButton";
    private const int WmNcLButtonDown = 0x00A1;
    private const int HtCaption = 0x0002;
    private static readonly Color PinActiveBackColor = Color.FromArgb(0, 120, 215);
    private static readonly Size FullMinimumSize = new(620, 260);
    private static readonly Size CompactMinimumSize = new(200, 120);

    private readonly AppSettings _settings;
    private readonly ApiClient _api;
    private readonly DataGridView _grid;
    private readonly ToolStripButton _pinButton;
    private readonly ToolStripButton _settingsButton;
    private readonly ToolStripButton _modeButton;
    private readonly ToolStrip _toolStrip;
    private readonly StatusStrip _statusStrip;
    private readonly ToolStripStatusLabel _syncLabel;
    private readonly ToolStripStatusLabel _deviceLabel;
    private readonly System.Windows.Forms.Timer _pollTimer;
    private readonly SemaphoreSlim _apiGate = new(1, 1);

    private StateSnapshot _state = new();
    private string _stateSignature = string.Empty;
    private bool _binding;
    private bool _syncingPin;
    private bool _allowExit;
    private bool _compact;
    private bool _syncIsOk;

    public event EventHandler? AlwaysOnTopChanged;

    // Raised after the interface language changed, so the tray menu can follow.
    public event EventHandler? LocalizationChanged;

    public MainForm(AppSettings settings, ApiClient api)
    {
        _settings = settings;
        _api = api;

        Text = Strings.AppTitle;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = settings.AlwaysOnTop;
        _compact = settings.CompactMode;
        RestoreWindowBounds();

        var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        if (icon is not null)
        {
            Icon = icon;
        }

        _toolStrip = new ToolStrip
        {
            GripStyle = ToolStripGripStyle.Hidden,
            Dock = DockStyle.Top,
            Padding = new Padding(4, 2, 4, 2)
        };

        // The stock checked-state rendering is too subtle to show that Pin is on, so a checked
        // toolbar button is filled with an accent color instead.
        _toolStrip.Renderer = new PinCheckedRenderer();

        // Project lifecycle lives in Settings now, so the toolbar only carries the mode switch,
        // Settings and Pin.
        _settingsButton = new ToolStripButton(Strings.ToolbarSettings);
        _settingsButton.Click += async (_, _) => await ShowSettingsDialogAsync();

        _modeButton = new ToolStripButton(Strings.ToolbarCompact)
        {
            ToolTipText = Strings.ToolbarCompactTooltip
        };
        _modeButton.Click += (_, _) => SetCompactMode(!_compact);

        _pinButton = new ToolStripButton(Strings.ToolbarPin)
        {
            // Named, because the caption is translated and the button is looked up by identity.
            Name = PinButtonName,
            CheckOnClick = true,
            Checked = settings.AlwaysOnTop,
            ToolTipText = Strings.ToolbarPinTooltipOff
        };
        _pinButton.CheckedChanged += (_, _) =>
        {
            if (!_syncingPin)
            {
                SetAlwaysOnTop(_pinButton.Checked);
            }
        };

        _toolStrip.Items.Add(_modeButton);
        _toolStrip.Items.Add(_settingsButton);
        _toolStrip.Items.Add(_pinButton);

        // Toolbar actions run against the row the user was working in, so a pending text edit is
        // committed on mouse down and the caret is parked on that same row.
        _settingsButton.MouseDown += (_, _) => CommitPendingGridEdit();
        _modeButton.MouseDown += (_, _) => CommitPendingGridEdit();

        // In compact mode the empty part of the toolbar works as a window handle too.
        _toolStrip.MouseDown += (_, e) =>
        {
            if (_compact && _toolStrip.GetItemAt(e.X, e.Y) is null)
            {
                BeginWindowDrag();
            }
        };

        UpdatePinVisual(settings.AlwaysOnTop);

        _grid = BuildGrid();
        _grid.Dock = DockStyle.Fill;

        _statusStrip = new StatusStrip();
        _syncLabel = new ToolStripStatusLabel(Strings.SyncNoConnection);
        var spring = new ToolStripStatusLabel { Spring = true };
        _deviceLabel = new ToolStripStatusLabel(Strings.DeviceLabelFormat(settings.LocalDeviceName));
        _statusStrip.Items.Add(_syncLabel);
        _statusStrip.Items.Add(spring);
        _statusStrip.Items.Add(_deviceLabel);

        Controls.Add(_grid);
        Controls.Add(_toolStrip);
        Controls.Add(_statusStrip);

        ApplyModeLayout();
        ApplyAppearance();
        ApplyLocalization();

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

        var minimum = _compact ? CompactMinimumSize : FullMinimumSize;
        var width = Math.Max(minimum.Width, bounds.Width);
        var height = Math.Max(minimum.Height, bounds.Height);

        // Full and Compact keep their own geometry, so switching modes does not move the other one.
        if (_compact)
        {
            _settings.CompactWindowX = bounds.X;
            _settings.CompactWindowY = bounds.Y;
            _settings.CompactWindowWidth = width;
            _settings.CompactWindowHeight = height;
        }
        else
        {
            _settings.WindowX = bounds.X;
            _settings.WindowY = bounds.Y;
            _settings.WindowWidth = width;
            _settings.WindowHeight = height;
        }

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
            _deviceLabel.Text = Strings.DeviceLabelFormat(_settings.LocalDeviceName);
            SetAlwaysOnTop(_settings.AlwaysOnTop);
            ApplyAppearance();

            // A language change applies straight away to this window and the tray menu; the Settings
            // dialog itself picks it up the next time it is opened.
            Localization.Apply(_settings.Language);
            ApplyLocalization();
            LocalizationChanged?.Invoke(this, EventArgs.Empty);
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

        // Compact hides the Updated column and the current cell cannot sit in a hidden column, so
        // the read-only Project cell is the parking spot there.
        _grid.CurrentCell = row.Cells[_compact ? NameColumn : UpdatedColumn];
    }

    public bool IsCompact => _compact;

    public void SetCompactMode(bool compact)
    {
        if (_compact == compact)
        {
            return;
        }

        // Store this mode's geometry before the layout changes, then bring the other one back.
        CommitPendingGridEdit();
        CaptureWindowSettings();

        _compact = compact;
        _settings.CompactMode = compact;
        AppSettingsStore.Save(_settings);

        ApplyModeLayout();
        ApplyAppearance();
        RestoreWindowBounds();
    }

    // One background colour for the ordinary surfaces, compact-only opacity, and text colour derived
    // from the background so labels stay readable. Status cells keep the colours the server assigns.
    private void ApplyAppearance()
    {
        var background = AppearanceSettings.ParseBackgroundColor(_settings.BackgroundColor);
        var custom = !background.IsEmpty;

        // Full mode is always fully opaque; the opacity setting belongs to Compact alone.
        Opacity = _compact ? AppearanceSettings.ToOpacity(_settings.CompactOpacity) : 1.0;

        var surface = custom ? background : SystemColors.Control;
        var text = custom ? GetContrastingTextColor(background) : SystemColors.ControlText;

        BackColor = surface;
        ForeColor = text;

        _toolStrip.BackColor = surface;
        _toolStrip.ForeColor = text;

        var gridBackground = custom ? background : SystemColors.Window;
        _grid.BackgroundColor = gridBackground;
        _grid.GridColor = custom ? ControlPaint.Dark(background) : SystemColors.ControlDark;
        _grid.EnableHeadersVisualStyles = !custom;
        _grid.DefaultCellStyle.BackColor = gridBackground;
        _grid.DefaultCellStyle.ForeColor = custom ? text : SystemColors.WindowText;
        _grid.DefaultCellStyle.SelectionBackColor = custom ? ControlPaint.Dark(background) : SystemColors.Highlight;
        _grid.DefaultCellStyle.SelectionForeColor = custom ? text : SystemColors.HighlightText;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = surface;
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = text;
        _grid.Invalidate();

        _statusStrip.BackColor = surface;
        _statusStrip.ForeColor = text;
        foreach (ToolStripItem item in _statusStrip.Items)
        {
            item.ForeColor = text;
        }

        // The sync label carries a state colour of its own; recompute it for the new background.
        ApplySyncLabel();
    }

    // Compact shows only the project and its status. Everything stays in the same window: no second
    // form, no second window type and no new top-level window.
    private void ApplyModeLayout()
    {
        // A maximized or snapped window would otherwise hand its geometry to the smaller layout.
        if (WindowState != FormWindowState.Normal)
        {
            WindowState = FormWindowState.Normal;
        }

        _grid.Columns[DeviceColumn].Visible = !_compact;
        _grid.Columns[NoteColumn].Visible = !_compact;
        _grid.Columns[UpdatedColumn].Visible = !_compact;
        _statusStrip.Visible = !_compact;
        _settingsButton.Visible = !_compact;
        _modeButton.Text = _compact ? Strings.ToolbarFull : Strings.ToolbarCompact;

        // Windows edge snapping needs WS_MAXIMIZEBOX, so dropping it disables snapping for this
        // window only. WS_THICKFRAME stays, which keeps the standard frame and manual resizing.
        // Full restores the flag, so native snapping behaves as usual again.
        MaximizeBox = !_compact;

        MinimumSize = _compact ? CompactMinimumSize : FullMinimumSize;
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

        // Read-only: a project is renamed in Settings, never by typing into the grid.
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = NameColumn,
            HeaderText = Strings.ColumnProject,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 24,
            ReadOnly = true
        });

        grid.Columns.Add(new DataGridViewComboBoxColumn
        {
            Name = StatusColumn,
            HeaderText = Strings.ColumnStatus,
            Width = 120,
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
            FlatStyle = FlatStyle.Flat
        });

        grid.Columns.Add(new DataGridViewComboBoxColumn
        {
            Name = DeviceColumn,
            HeaderText = Strings.ColumnDevice,
            Width = 100,
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
            FlatStyle = FlatStyle.Flat
        });

        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = NoteColumn,
            HeaderText = Strings.ColumnNote,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 38,
            MaxInputLength = 200
        });

        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = UpdatedColumn,
            HeaderText = Strings.ColumnUpdated,
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

        // A click on the empty area below the rows ends the edit like Enter does. In compact mode the
        // empty area and the header strip also work as a window handle, because the small window
        // leaves little else to grab for moving it.
        grid.MouseDown += (_, e) =>
        {
            var hit = grid.HitTest(e.X, e.Y);
            if (hit.Type == DataGridViewHitTestType.None)
            {
                CommitPendingGridEdit();
            }

            if (_compact &&
                hit.Type is DataGridViewHitTestType.None or DataGridViewHitTestType.ColumnHeader)
            {
                BeginWindowDrag();
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
            // Hidden projects never take part in the main-grid binding, in Full and in Compact.
            // They stay on the server and in Settings; the regular poll picks up hide/unhide.
            foreach (var project in ProjectVisibility.VisibleOnly(_state.Projects))
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
            ShowError(Strings.ErrorProjectNameBlank);
            await RefreshStateAsync(force: true);
            return;
        }

        if (note.Length > 200 || note.Contains('\r') || note.Contains('\n'))
        {
            ShowError(Strings.ErrorNoteInvalid);
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
            // The grid never edits visibility itself, but the current flag is sent back so a
            // status/device/note change cannot accidentally unhide the row.
            var updated = await _api.UpdateProjectAsync(current.Id, new ProjectPayload
            {
                Name = name,
                StatusId = statusId,
                DeviceId = deviceId,
                Note = note,
                IsHidden = current.IsHidden
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

    private static int? ParseNullableId(object? value)
    {
        var text = Convert.ToString(value, CultureInfo.InvariantCulture);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            ? id
            : null;
    }

    private void SetSyncOk()
    {
        _syncIsOk = true;
        ApplySyncLabel();
    }

    private void SetSyncOffline()
    {
        _syncIsOk = false;
        ApplySyncLabel();
    }

    // The state colours are the readable pair for whichever background is in use.
    private void ApplySyncLabel()
    {
        var custom = !AppearanceSettings.ParseBackgroundColor(_settings.BackgroundColor).IsEmpty;

        _syncLabel.Text = _syncIsOk ? Strings.SyncOk : Strings.SyncNoConnection;
        _syncLabel.ForeColor = _syncIsOk
            ? (custom ? Color.FromArgb(126, 231, 135) : Color.DarkGreen)
            : (custom ? Color.FromArgb(255, 138, 128) : Color.Firebrick);
    }

    private void SetSyncOfflineIfNetwork(Exception ex)
    {
        if (ex is HttpRequestException or TaskCanceledException)
        {
            SetSyncOffline();
        }
    }

    // The month name follows the interface language, not the OS culture, so Apply("ru") renders
    // Russian month names and Apply("en") renders English ones. Exposed for tests (see
    // InternalsVisibleTo in the project file); parsing stays invariant because the server sends
    // round-trip ISO timestamps.
    internal static string FormatUpdated(string value)
    {
        if (!DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed))
        {
            return value;
        }

        return parsed.ToLocalTime().ToString("dd MMM HH:mm", Localization.Culture);
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
                .Append(project.Note).Append(':').Append(project.IsHidden).Append(':')
                .Append(project.UpdatedAt).Append('|');
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
            ? Strings.ToolbarPinTooltipOn
            : Strings.ToolbarPinTooltipOff;
    }

    // Every user-visible caption of this window lives here, so the language can be changed without
    // restarting the application.
    public void ApplyLocalization()
    {
        Text = Strings.AppTitle;
        _settingsButton.Text = Strings.ToolbarSettings;
        _pinButton.Text = Strings.ToolbarPin;
        _modeButton.Text = _compact ? Strings.ToolbarFull : Strings.ToolbarCompact;
        _modeButton.ToolTipText = Strings.ToolbarCompactTooltip;
        UpdatePinVisual(_pinButton.Checked);

        _grid.Columns[NameColumn].HeaderText = Strings.ColumnProject;
        _grid.Columns[StatusColumn].HeaderText = Strings.ColumnStatus;
        _grid.Columns[DeviceColumn].HeaderText = Strings.ColumnDevice;
        _grid.Columns[NoteColumn].HeaderText = Strings.ColumnNote;
        _grid.Columns[UpdatedColumn].HeaderText = Strings.ColumnUpdated;

        _deviceLabel.Text = Strings.DeviceLabelFormat(_settings.LocalDeviceName);
        ApplySyncLabel();
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    // Compact mode has almost no chrome left to grab, so a press on its empty surface is handed to
    // the system's own caption drag. That keeps TopMost, the standard frame and the no-snap styles
    // untouched.
    private void BeginWindowDrag()
    {
        if (!_compact || !Visible)
        {
            return;
        }

        ReleaseCapture();
        SendMessage(Handle, WmNcLButtonDown, HtCaption, IntPtr.Zero);
    }

    private void RestoreWindowBounds()
    {
        var minimum = _compact ? CompactMinimumSize : FullMinimumSize;
        MinimumSize = minimum;

        var savedX = _compact ? _settings.CompactWindowX : _settings.WindowX;
        var savedY = _compact ? _settings.CompactWindowY : _settings.WindowY;
        var savedWidth = _compact ? _settings.CompactWindowWidth : _settings.WindowWidth;
        var savedHeight = _compact ? _settings.CompactWindowHeight : _settings.WindowHeight;

        var primary = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        var workingAreas = Screen.AllScreens.Select(screen => screen.WorkingArea).ToList();

        Bounds = WindowBounds.Resolve(
            savedX,
            savedY,
            savedWidth,
            savedHeight,
            minimum,
            primary,
            workingAreas);
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
        MessageBox.Show(this, message, Strings.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
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
