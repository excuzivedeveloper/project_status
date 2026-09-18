using System.IO.Pipes;
using System.Security.Principal;
using System.Text;

namespace ProjectStatus.Client;

// Keeps one Project Status instance per Windows user.
//
// The first process owns a per-user named mutex and serves a per-user named pipe. A later launch
// notices the mutex, hands an activation request to the running instance and exits. Everything stays
// local: no TCP, no ports, no admin rights, and every kernel object name carries the current user's
// SID so a different user on the same machine cannot reach it.
internal sealed class SingleInstance : IDisposable
{
    private const string MutexNamePrefix = "Local\\ProjectStatus.Client.SingleInstance.";
    private const string PipeNamePrefix = "ProjectStatus.Client.Activate.";
    private const string ActivateCommand = "ACTIVATE";
    private const int ActivationAttempts = 8;
    private static readonly TimeSpan ActivationTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ActivationRetryDelay = TimeSpan.FromMilliseconds(250);

    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _cancellation = new();
    private bool _disposed;

    private SingleInstance(Mutex mutex, string pipeName, bool isPrimaryInstance)
    {
        _mutex = mutex;
        _pipeName = pipeName;
        IsPrimaryInstance = isPrimaryInstance;
    }

    public bool IsPrimaryInstance { get; }

    public static SingleInstance Acquire()
    {
        var identity = CurrentUserIdentity();
        var pipeName = PipeNamePrefix + identity;

        // createdNew == false means another process already holds the mutex, which is the liveness
        // signal wanted here: a named mutex is destroyed once its last handle closes, so a killed
        // instance does not leave a stale lock behind.
        var mutex = new Mutex(initiallyOwned: true, MutexNamePrefix + identity, out var createdNew);
        return new SingleInstance(mutex, pipeName, createdNew);
    }

    // Asks the running instance to bring its window forward. Returns false when nobody answered.
    public static bool RequestActivation()
    {
        var pipeName = PipeNamePrefix + CurrentUserIdentity();

        for (var attempt = 0; attempt < ActivationAttempts; attempt++)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
                client.Connect((int)ActivationTimeout.TotalMilliseconds);

                using var writer = new StreamWriter(client, new UTF8Encoding(false));
                writer.WriteLine(ActivateCommand);
                writer.Flush();
                return true;
            }
            catch (TimeoutException)
            {
                // The running instance may still be starting up; retry below.
            }
            catch (Exception)
            {
                // Activation is best effort: the caller exits either way.
            }

            Thread.Sleep(ActivationRetryDelay);
        }

        return false;
    }

    public void StartListening(Action onActivateRequested)
    {
        if (_disposed)
        {
            return;
        }

        _ = Task.Run(() => ListenAsync(onActivateRequested));
    }

    private async Task ListenAsync(Action onActivateRequested)
    {
        var token = _cancellation.Token;

        while (!token.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(token).ConfigureAwait(false);

                using var reader = new StreamReader(server, new UTF8Encoding(false));
                var command = await reader.ReadLineAsync(token).ConfigureAwait(false);

                if (string.Equals(command?.Trim(), ActivateCommand, StringComparison.Ordinal))
                {
                    onActivateRequested();
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception)
            {
                // The peer may vanish mid-handshake. Serving the next launch must keep working and
                // the listener must never take the application down.
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private static string CurrentUserIdentity()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var sid = identity.User?.Value;
            if (!string.IsNullOrWhiteSpace(sid))
            {
                return sid;
            }
        }
        catch (Exception)
        {
            // Fall back to the account name below.
        }

        return Environment.UserName;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        _cancellation.Dispose();

        if (IsPrimaryInstance)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (Exception)
            {
                // Releasing is best effort; disposing the handle is what frees the name.
            }
        }

        _mutex.Dispose();
    }
}
