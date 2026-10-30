using FinanceApp.Application.Backups;
using FinanceApp.Presentation.Data;
using Microsoft.Extensions.Time.Testing;

namespace FinanceApp.Core.Tests.Presentation;

public class DataViewModelTests
{
    private readonly FakeData _backend = new();
    private readonly FakeSharer _sharer = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero));

    private DataViewModel Open()
    {
        _time.SetLocalTimeZone(TimeZoneInfo.Utc);
        var viewModel = new DataViewModel(_backend, _sharer, _time);
        viewModel.Load();
        return viewModel;
    }

    [Fact]
    public void Load_ListsCopies_NewestFirst()
    {
        _backend.Copies.Add(new BackupInfo("/data/backups/finance-2026-10-15_090000.db", new DateTime(2026, 10, 15, 9, 0, 0), 2_048));
        _backend.Copies.Add(new BackupInfo("/data/backups/finance-2026-10-16_093000.db", new DateTime(2026, 10, 16, 9, 30, 0), 1_258_291));

        var viewModel = Open();

        viewModel.Backups.Select(b => b.Title).ShouldBe(["Сегодня, 09:30", "15 октября 2026, 09:00"]);
        viewModel.BackupsText.ShouldBe("Копий на телефоне: 2, последняя — сегодня, 09:30.");
    }

    [Fact]
    public void NoCopies_SaysSo() =>
        Open().BackupsText.ShouldStartWith("Копий пока нет.");

    [Fact]
    public async Task ShareCsv_ExportsIntoTheShareFolder_AndSharesBothFiles()
    {
        var viewModel = Open();

        await viewModel.ShareCsvCommand.ExecuteAsync(null);

        _backend.ExportedTo.ShouldBe("/cache/share");
        _sharer.Shared.ShouldHaveSingleItem().ShouldBe(["/cache/share/transactions.csv", "/cache/share/accounts.csv"]);
        viewModel.Status.ShouldBe("Выгружено операций: 13.");
        viewModel.Failed.ShouldBeFalse();
    }

    [Fact]
    public async Task ShareBackup_MakesACopyNow_AndSharesIt()
    {
        var viewModel = Open();

        await viewModel.ShareBackupCommand.ExecuteAsync(null);

        _sharer.Shared.ShouldHaveSingleItem().ShouldBe(["/data/backups/finance-2026-10-16_120000.db"]);
        viewModel.Backups.ShouldHaveSingleItem().Title.ShouldBe("Сегодня, 12:00");
        viewModel.Status.ShouldBe("Копия сделана: finance-2026-10-16_120000.db, 4 КБ.");
    }

    [Fact]
    public async Task Failure_IsShown_AndTheScreenStaysUsable()
    {
        _sharer.Failure = new IOException("нет места");
        var viewModel = Open();

        await viewModel.ShareCsvCommand.ExecuteAsync(null);

        viewModel.Failed.ShouldBeTrue();
        viewModel.Status.ShouldBe("Не получилось: нет места");
        viewModel.IsBusy.ShouldBeFalse();
        viewModel.ShareBackupCommand.CanExecute(null).ShouldBeTrue();
    }

    private sealed class FakeData : IDataBackend
    {
        public List<BackupInfo> Copies { get; } = [];

        public string? ExportedTo { get; private set; }

        public Task<ExportedCsv> ExportCsvAsync(string folder, CancellationToken cancellationToken = default)
        {
            ExportedTo = folder;
            return Task.FromResult(new ExportedCsv(13, [$"{folder}/transactions.csv", $"{folder}/accounts.csv"]));
        }

        public Task<BackupInfo> BackupAsync(CancellationToken cancellationToken = default)
        {
            var copy = new BackupInfo("/data/backups/finance-2026-10-16_120000.db", new DateTime(2026, 10, 16, 12, 0, 0), 4096);
            Copies.Add(copy);
            return Task.FromResult(copy);
        }

        public IReadOnlyList<BackupInfo> ListBackups() => Copies.OrderByDescending(c => c.CreatedLocal).ToList();
    }

    private sealed class FakeSharer : IFileSharer
    {
        public List<IReadOnlyList<string>> Shared { get; } = [];

        public Exception? Failure { get; set; }

        public string ShareFolder => "/cache/share";

        public Task ShareAsync(string title, IReadOnlyList<string> paths)
        {
            if (Failure is not null)
            {
                return Task.FromException(Failure);
            }

            Shared.Add(paths);
            return Task.CompletedTask;
        }
    }
}
