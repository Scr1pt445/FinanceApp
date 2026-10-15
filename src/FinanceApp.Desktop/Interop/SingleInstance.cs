using System.Runtime.InteropServices;

namespace FinanceApp.Desktop.Interop;

/// <summary>
/// One app per Windows session. A second launch signals the first one (which opens the quick-entry
/// window) and exits — handy when the app is started again from the Start menu.
/// </summary>
internal sealed partial class SingleInstance : IDisposable
{
    private const string Name = @"Local\FinanceApp.Desktop";
    private const int AsfwAny = -1;

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activate;
    private readonly RegisteredWaitHandle? _wait;

    private SingleInstance(Mutex mutex, EventWaitHandle activate, Action onActivate)
    {
        _mutex = mutex;
        _activate = activate;
        _wait = ThreadPool.RegisterWaitForSingleObject(_activate, (_, _) => onActivate(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    /// <returns>The guard for the first instance, or <c>null</c> after signalling the running one.</returns>
    public static SingleInstance? TryAcquire(Action onActivate)
    {
        var mutex = new Mutex(initiallyOwned: true, Name, out var createdNew);
        var activate = new EventWaitHandle(false, EventResetMode.AutoReset, Name + ".Activate");
        if (createdNew)
        {
            return new SingleInstance(mutex, activate, onActivate);
        }

        // We were launched by the user, so we may hand foreground rights to the running instance;
        // without this Windows shows its window but keeps the keyboard focus elsewhere.
        AllowSetForegroundWindow(AsfwAny);
        activate.Set();
        activate.Dispose();
        mutex.Dispose();
        return null;
    }

    public void Dispose()
    {
        _wait?.Unregister(null);
        _activate.Dispose();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(int processId);
}
