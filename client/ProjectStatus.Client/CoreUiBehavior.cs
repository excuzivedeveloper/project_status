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
        var toolStrip = FindControl<ToolStrip>(form);
        if (toolStrip is null)
        {
            return;
        }

        // Found by name: the caption is localised, the identity of the button is not.
        var pinButton = toolStrip.Items
            .OfType<ToolStripButton>()
            .FirstOrDefault(item => string.Equals(item.Name, "PinButton", StringComparison.Ordinal));

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

            // Settings and the other owned dialogs are modal. The grid edit has to be committed
            // before the modal loop takes over: the main form cannot end it while a dialog owns the
            // message loop, and a session left open there keeps the grid from being refreshed.
            form.CommitPendingGridEdit();

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
