using System.Threading.Channels;
using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanceApp.Application.Export;

/// <summary>Runs one export in its own unit of work (a fresh database context).</summary>
public interface ICsvExportJob
{
    Task<int> RunAsync(string path, CancellationToken cancellationToken);
}

public sealed record CsvExportOptions
{
    /// <summary>Several quick changes in a row produce one write.</summary>
    public TimeSpan Debounce { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Pause before retrying when the file is busy (Obsidian or sync is reading it).</summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(2);

    public int MaxAttempts { get; init; } = 3;
}

public enum CsvExportState
{
    Idle,
    Succeeded,
    Failed,
}

public sealed record CsvExportStatus(CsvExportState State, DateTime? LastSuccessUtc = null, int Rows = 0, string? Error = null)
{
    public static CsvExportStatus Idle { get; } = new(CsvExportState.Idle);
}

/// <summary>
/// Keeps the CSV file in sync with the database (requirements, section 5): after each change the
/// whole file is rewritten in the background, with debounce and retries. Failures become a status
/// for the UI, never an exception for the code that saved a transaction.
/// </summary>
public sealed class CsvExportCoordinator : IDataChangedListener, IAsyncDisposable
{
    private readonly ICsvExportJob _job;
    private readonly ISettingsStore _settings;
    private readonly CsvExportOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly Channel<bool> _signals = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _loop;
    private int _dirty;
    private int _disposed;

    public CsvExportCoordinator(
        ICsvExportJob job,
        ISettingsStore settings,
        CsvExportOptions options,
        TimeProvider timeProvider,
        ILogger<CsvExportCoordinator>? logger = null)
    {
        _job = job;
        _settings = settings;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger ?? NullLogger<CsvExportCoordinator>.Instance;
        _loop = Task.Run(RunLoopAsync);
    }

    public CsvExportStatus Status { get; private set; } = CsvExportStatus.Idle;

    public event EventHandler<CsvExportStatus>? StatusChanged;

    public void OnDataChanged()
    {
        if (!_settings.Load().CsvExport.IsActive)
        {
            return;
        }

        Interlocked.Exchange(ref _dirty, 1);
        _signals.Writer.TryWrite(true);
    }

    /// <summary>Exports right now if there are unexported changes (e.g. before a CLI process exits).</summary>
    public Task FlushAsync(CancellationToken cancellationToken = default) => ExportIfDirtyAsync(cancellationToken);

    /// <summary>Exports unconditionally (initial export after enabling the integration).</summary>
    public Task<CsvExportStatus> ExportNowAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Exchange(ref _dirty, 1);
        return ExportIfDirtyAsync(cancellationToken);
    }

    /// <remarks>
    /// Idempotent: DI may dispose the same instance through several registrations. Never resumes on the
    /// caller's context: the desktop app disposes on the UI thread and blocks it until disposal ends.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _signals.Writer.TryComplete();
        await _shutdown.CancelAsync().ConfigureAwait(false);
        try
        {
            await _loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        _shutdown.Dispose();
        _gate.Dispose();
    }

    private async Task RunLoopAsync()
    {
        var token = _shutdown.Token;
        while (await _signals.Reader.WaitToReadAsync(token))
        {
            _signals.Reader.TryRead(out _);
            await Task.Delay(_options.Debounce, _timeProvider, token);
            await ExportIfDirtyAsync(token);
        }
    }

    private async Task<CsvExportStatus> ExportIfDirtyAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (Interlocked.Exchange(ref _dirty, 0) == 0)
            {
                return Status;
            }

            var export = _settings.Load().CsvExport;
            if (!export.IsActive)
            {
                return Status;
            }

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    var rows = await _job.RunAsync(export.Path!, cancellationToken);
                    SetStatus(new CsvExportStatus(CsvExportState.Succeeded, _timeProvider.GetUtcNow().UtcDateTime, rows));
                    return Status;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < _options.MaxAttempts)
                {
                    _logger.LogWarning(ex, "CSV export attempt {Attempt} failed, retrying", attempt);
                    await Task.Delay(_options.RetryDelay, _timeProvider, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "CSV export to {Path} failed", export.Path);

                    // Keep the changes pending: the next change or flush tries again.
                    Interlocked.Exchange(ref _dirty, 1);
                    SetStatus(Status with { State = CsvExportState.Failed, Error = $"Не удалось записать CSV: {ex.Message}" });
                    return Status;
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private void SetStatus(CsvExportStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(this, status);
    }
}
