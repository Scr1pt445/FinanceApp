using Microsoft.Maui.Dispatching;

namespace FinanceApp.Mobile.Tests;

/// <summary>
/// The phone's UI thread, as far as the pages can tell: views may be created on any test thread, and work
/// sent to the dispatcher runs at once.
/// </summary>
internal sealed class ImmediateDispatcher : IDispatcher, IDispatcherProvider
{
    public static readonly ImmediateDispatcher Instance = new();

    public bool IsDispatchRequired => false;

    public static void Install() => DispatcherProvider.SetCurrent(Instance);

    public IDispatcher? GetForCurrentThread() => this;

    public bool Dispatch(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        action();
        return true;
    }

    public bool DispatchDelayed(TimeSpan delay, Action action)
    {
        _ = Task.Delay(delay).ContinueWith(_ => action(), TaskScheduler.Default);
        return true;
    }

    public IDispatcherTimer CreateTimer() => throw new NotSupportedException("The pages do not use timers.");
}
