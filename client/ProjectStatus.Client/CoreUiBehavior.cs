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
            if (hit.RowIndex < 0 && hit.ColumnIndex < 0)
            {
                EndEditIfNeeded(grid);
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

        foreach (var button in toolStrip.Items.OfType<ToolStripButton>())
        {
            if (button.Text is not ("+ Project" or "Delete" or "Settings"))
            {
                continue;
            }

            button.MouseDown += (_, _) =>
            {
                EndEditIfNeeded(grid);
                SuspendTopMostForModal(form, pinButton);
            };
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

        grid.BeginInvoke(new Action(() => EndEditIfNeeded(grid)));
    }

    private static void EndEditIfNeeded(DataGridView grid)
    {
        if (grid.IsDisposed || !grid.IsCurrentCellInEditMode)
        {
            return;
        }

        grid.EndEdit();
    }

    private static void SuspendTopMostForModal(MainForm form, ToolStripButton? pinButton)
    {
        if (form.IsDisposed || !form.TopMost)
        {
            return;
        }

        // Do not call SetAlwaysOnTop here: this is only a temporary modal-dialog
        // accommodation and must not change the persisted Pin preference.
        form.TopMost = false;

        var timer = new System.Windows.Forms.Timer { Interval = 100 };
        timer.Tick += (_, _) =>
        {
            if (form.IsDisposed)
            {
                timer.Stop();
                timer.Dispose();
                return;
            }

            // A modal child disables its owner. Wait until the dialog is gone before
            // restoring TopMost so the child can never end up hidden behind the owner.
            if (!form.Enabled)
            {
                return;
            }

            timer.Stop();
            timer.Dispose();

            // Settings may have changed Always on top while the owner was suspended.
            // Restore only when Pin is still meant to be enabled.
            if (pinButton?.Checked == true)
            {
                form.TopMost = true;
            }
        };
        timer.Start();
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
