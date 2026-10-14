using FinanceApp.Application.Export;
using FinanceApp.Application.Settings;

namespace FinanceApp.Core.Tests.Export;

public class CsvExportCoordinatorTests
{
    private static readonly CsvExportOptions FastOptions = new()
    {
        Debounce = TimeSpan.FromMilliseconds(50),
        RetryDelay = TimeSpan.FromMilliseconds(10),
        MaxAttempts = 3,
    };

    private readonly FakeSettings _settings = new() { Value = new AppSettings { CsvExport = new() { Enabled = true, Path = "out.csv" } } };
    private readonly FakeJob _job = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private CsvExportCoordinator Create() => new(_job, _settings, FastOptions, TimeProvider.System);

    [Fact]
    public async Task BurstOfChanges_IsDebouncedIntoOneExport()
    {
        await using var coordinator = Create();
        var exported = new TaskCompletionSource<CsvExportStatus>();
        coordinator.StatusChanged += (_, status) => exported.TrySetResult(status);

        for (var i = 0; i < 5; i++)
        {
            coordinator.OnDataChanged();
        }

        (await exported.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct)).State.ShouldBe(CsvExportState.Succeeded);
        await Task.Delay(200, Ct);
        _job.Paths.ShouldBe(["out.csv"]);
    }

    [Fact]
    public async Task Disabled_DoesNothing()
    {
        _settings.Value = new AppSettings();
        await using var coordinator = Create();

        coordinator.OnDataChanged();
        await coordinator.FlushAsync(Ct);

        _job.Paths.ShouldBeEmpty();
        coordinator.Status.State.ShouldBe(CsvExportState.Idle);
    }

    [Fact]
    public async Task BusyFile_IsRetriedUntilItSucceeds()
    {
        _job.FailuresBeforeSuccess = 2;
        await using var coordinator = Create();

        var status = await coordinator.ExportNowAsync(Ct);

        status.State.ShouldBe(CsvExportState.Succeeded);
        status.Rows.ShouldBe(42);
        _job.Paths.Count.ShouldBe(3);
    }

    [Fact]
    public async Task PersistentFailure_BecomesStatus_AndStaysPendingForNextFlush()
    {
        _job.FailuresBeforeSuccess = int.MaxValue;
        await using var coordinator = Create();

        var status = await coordinator.ExportNowAsync(Ct);

        status.State.ShouldBe(CsvExportState.Failed);
        status.Error.ShouldNotBeNull().ShouldContain("занят");
        _job.Paths.Count.ShouldBe(FastOptions.MaxAttempts);

        _job.FailuresBeforeSuccess = 0;
        await coordinator.FlushAsync(Ct);
        coordinator.Status.State.ShouldBe(CsvExportState.Succeeded);
    }

    [Fact]
    public async Task Flush_WithoutChanges_DoesNotExport()
    {
        await using var coordinator = Create();

        await coordinator.FlushAsync(Ct);

        _job.Paths.ShouldBeEmpty();
    }

    private sealed class FakeJob : ICsvExportJob
    {
        private readonly Lock _lock = new();

        public List<string> Paths { get; } = [];

        public int FailuresBeforeSuccess { get; set; }

        public Task<int> RunAsync(string path, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                Paths.Add(path);
                if (FailuresBeforeSuccess > 0)
                {
                    FailuresBeforeSuccess--;
                    throw new IOException("файл занят");
                }
            }

            return Task.FromResult(42);
        }
    }

    private sealed class FakeSettings : ISettingsStore
    {
        public AppSettings Value { get; set; } = new();

        public AppSettings Load() => Value;

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            Value = settings;
            return Task.CompletedTask;
        }
    }
}
