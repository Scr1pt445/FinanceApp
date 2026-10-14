using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanceApp.Application.Abstractions;

/// <summary>Tells all <see cref="IDataChangedListener"/>s about a commit; a failing listener is logged, never rethrown.</summary>
public sealed class DataChangedNotifier(IEnumerable<IDataChangedListener> listeners, ILogger<DataChangedNotifier>? logger = null)
{
    private readonly ILogger _logger = logger ?? NullLogger<DataChangedNotifier>.Instance;

    public void NotifyChanged()
    {
        foreach (var listener in listeners)
        {
            try
            {
                listener.OnDataChanged();
            }
#pragma warning disable CA1031 // An integration must never break saving data.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                _logger.LogError(ex, "Data-changed listener {Listener} failed", listener.GetType().Name);
            }
        }
    }
}
