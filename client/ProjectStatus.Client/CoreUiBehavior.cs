namespace ProjectStatus.Client;

internal static class CoreUiBehavior
{
    private static EventHandler? _idleHandler;

    public static void Install()
    {
        if (_idleHandler is not null)
        {
            return;
        }

        _idleHandler = (_, _) =>
        {
            var mainForm = Application.OpenForms.OfType<MainForm>().FirstOrDefault();
            if (mainForm is null)
            {
                return;
            }

            Attach(mainForm);
            Application.Idle -= _idleHandler;
            _idleHandler = null;
        };

        Application.Idle += _idleHandler;
    }

    private static void Attach(MainForm form)
    {
        var grid = FindControl<DataGridView>(form);
        var toolStrip = FindControl<ToolStrip>(form);
        if (grid is null || toolStrip is null)
        {
            return;
        }

        grid.Leave += (_, _) => EndEditIfNeeded(grid);
        form.Deactivate += (_, _) => EndEditIfNeeded(grid);

        grid.MouseDown += (_, e) =>
        {
            var hit = grid.HitTest(e.X, e.Y);
            if (hit.Type == DataGridViewHitTestType.None)
            {
                CommitAndLeaveCurrentCell(grid);
            }
        };

        grid.EditingControlShowing += (_, e) =>
        {
            if (e.Control is not TextBox textBox)
            {
                return;
            }

            textBox.KeyDown -= OnEditingTextBoxKeyDown;
            textBox.KeyDown += OnEditingTextBoxKeyDown;
        };

        var pinButton = toolStrip.Items
            .OfType<ToolStripButton>()
            .FirstOrDefault(item => string.Equals(item.Text, "Pin", StringComparison.Ordinal));

        var modalDepth = 0;
        var topMostSuspendedForModal = false;

        EventHandler? enterModalHandler = null;
        EventHandler? leaveModalHandler = null;
        EventHandler? disposedHandler = null;

        enterModalHandler = (_, _) =>
        {
            modalDepth++;
            if (modalDepth != 1 || form.IsDisposed)
            {
                return;
            }

            EndEditIfNeeded(grid);
            if (form.TopMost)
            {
                // WinForms exposes modal-loop lifecycle directly. Temporarily drop
                // TopMost on the owner for the outermost modal loop so owned dialogs
                // cannot be hidden behind it. Do not call SetAlwaysOnTop because the
                // persisted Pin preference must remain unchanged.
                form.TopMost = false;
                topMostSuspendedForModal = true;
            }
        };

        leaveModalHandler = (_, _) =>
        {
            if (modalDepth > 0)
            {
                modalDepth--;
            }

            if (modalDepth != 0 || form.IsDisposed || !topMostSuspendedForModal)
            {
                return;
            }

            topMostSuspendedForModal = false;
            if (pinButton?.Checked == true)
            {
                form.TopMost = true;
            }
        };

        disposedHandler = (_, _) =>
        {
            if (enterModalHandler is not null)
            {
                Application.EnterThreadModal -= enterModalHandler;
            }

            if (leaveModalHandler is not null)
            {
                Application.LeaveThreadModal -= leaveModalHandler;
            }

            if (disposedHandler is not null)
            {
                form.Disposed -= disposedHandler;
            }
        };

        Application.EnterThreadModal += enterModalHandler;
        Application.LeaveThreadModal += leaveModalHandler;
        form.Disposed += disposedHandler;

        foreach (var button in toolStrip.Items.OfType<ToolStripButton>())
        {
            if (button.Text is not ("+ Project" or "Delete" or "Settings"))
            {
                continue;
            }

            // Toolbar actions must save an in-progress text edit but keep the current
            // row selected so Delete and other row-oriented actions still know which
            // project the user was working with.
            button.MouseDown += (_, _) => EndEditIfNeeded(grid);
        }
    }

    private static void OnEditingTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter || sender is not Control editingControl)
        {
            return;
        }

        e.Handled = true;
        e.SuppressKeyPress = true;

        var grid = FindParentGrid(editingControl);
        if (grid is null || grid.IsDisposed)
        {
            return;
        }

        // Run after the key event unwinds so DataGridView can finish its own editing
        // control processing, then visibly leave the edited cell after committing it.
        grid.BeginInvoke(new Action(() => CommitAndLeaveCurrentCell(grid)));
    }

    private static void CommitAndLeaveCurrentCell(DataGridView grid)
    {
        if (grid.IsDisposed)
        {
            return;
        }

        if (grid.IsCurrentCellInEditMode && !grid.EndEdit())
        {
            return;
        }

        // EndEdit commits the value but DataGridView intentionally keeps CurrentCell
        // selected. Clearing it makes Enter / blank-area click behave like a completed
        // edit instead of leaving the edited cell visually active.
        grid.CurrentCell = null;
        grid.ClearSelection();
    }

    private static void EndEditIfNeeded(DataGridView grid)
    {
        if (grid.IsDisposed || !grid.IsCurrentCellInEditMode)
        {
            return;
        }

        grid.EndEdit();
    }

    private static DataGridView? FindParentGrid(Control control)
    {
        for (Control? current = control.Parent; current is not null; current = current.Parent)
        {
            if (current is DataGridView grid)
            {
                return grid;
            }
        }

        return null;
    }

    private static T? FindControl<T>(Control root) where T : Control
    {
        if (root is T match)
        {
            return match;
        }

        foreach (Control child in root.Controls)
        {
            var nested = FindControl<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }
}
