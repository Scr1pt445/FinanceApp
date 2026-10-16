using System.Runtime.InteropServices;

namespace FinanceApp.Desktop.Interop;

/// <summary>What a second launch asks the running instance to do.</summary>
internal enum InstanceRequest
{
    /// <summary>Plain start (the Start menu, the exe): open quick entry.</summary>
    QuickEntry,

    /// <summary><c>--main</c>: open the main window.</summary>
    Main,

    /// <summary><c>--exit</c>: quit cleanly — the tray icon goes away and a pending CSV export is written.</summary>
    Exit,
}

/// <summary>
/// One app per Windows session. A second launch passes its request to the first one and exits, so starting the
/// app again from the Start menu brings it up, and <c>--exit</c> closes it (used by the installer).
/// </summary>
internal sealed partial class SingleInstance : IDisposable
{
    private const string Name = @"Local\FinanceApp.Desktop";
    private const int AsfwAny = -1;

    private readonly Mutex _mutex;
    private readonly (EventWaitHandle Signal, RegisteredWaitHandle Wait)[] _handlers;

    private SingleInstance(Mutex mutex, Action<InstanceRequest> onRequest)
    {
        _mutex = mutex;
        _handlers = Enum.GetValues<InstanceRequest>()
            .Select(request =>
            {
                var signal = Signal(request);
                var wait = ThreadPool.RegisterWaitForSingleObject(signal, (_, _) => onRequest(request), null, Timeout.Infinite, executeOnlyOnce: false);
                return (signal, wait);
            })
            .ToArray();
    }

    /// <param name="request">What this launch wants; passed to the running instance if there is one.</param>
    /// <returns>
    /// The guard for the first instance, or <c>null</c> after passing the request on — and always <c>null</c>
    /// for <see cref="InstanceRequest.Exit"/>: there is nothing to start.
    /// </returns>
    public static SingleInstance? TryAcquire(InstanceRequest request, Action<InstanceRequest> onRequest)
    {
        var mutex = new Mutex(initiallyOwned: true, Name, out var createdNew);
        if (createdNew && request != InstanceRequest.Exit)
        {
            return new SingleInstance(mutex, onRequest);
        }

        if (!createdNew)
        {
            // We were launched by the user, so we may hand foreground rights to the running instance;
            // without this Windows shows its window but keeps the keyboard focus elsewhere.
            AllowSetForegroundWindow(AsfwAny);
            using var signal = Signal(request);
            signal.Set();
        }
        else
        {
            mutex.ReleaseMutex();
        }

        mutex.Dispose();
        return null;
    }

    public void Dispose()
    {
        foreach (var (signal, wait) in _handlers)
        {
            wait.Unregister(null);
            signal.Dispose();
        }

        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }

    private static EventWaitHandle Signal(InstanceRequest request) =>
        new(false, EventResetMode.AutoReset, request == InstanceRequest.QuickEntry ? Name + ".Activate" : $"{Name}.{request}");

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(int processId);
}
