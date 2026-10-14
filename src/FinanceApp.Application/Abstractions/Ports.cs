namespace FinanceApp.Application.Abstractions;

/// <summary>Writes files so that a reader never sees a half-written file.</summary>
public interface IFileWriter
{
    /// <summary>Writes to a temporary file in the same folder, then replaces the target.</summary>
    /// <exception cref="IOException">The target is locked or the folder is not writable.</exception>
    Task WriteAtomicallyAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken = default);
}

/// <summary>
/// Notified after transactions, categories or accounts were committed. Integrations (CSV export)
/// subscribe here, so the core knows nothing about them. Must return quickly and must not throw.
/// </summary>
public interface IDataChangedListener
{
    void OnDataChanged();
}
