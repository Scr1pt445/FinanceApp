using FinanceApp.Application.Backups;
using FinanceApp.Application.Export;
using FinanceApp.Application.Settings;
using FinanceApp.Presentation.Settings;
using Microsoft.Extensions.Time.Testing;

namespace FinanceApp.Core.Tests.Presentation;

public class SettingsViewModelTests
{
    private readonly FakeBackend _backend = new();
    private readonly FakeBackups _backups = new();
    private readonly FakeAutostart _autostart = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private SettingsViewModel Open()
    {
        _time.SetLocalTimeZone(TimeZoneInfo.Utc);
        var viewModel = new SettingsViewModel(_backend, _backups, _autostart, new Registrar(), _time);
        viewModel.Load();
        return viewModel;
    }

    [Fact]
    public void Load_ShowsCurrentState()
    {
        _backend.Settings = new AppSettings { CsvExport = new CsvExportSettings { Enabled = true, Path = @"C:\Notes\t.csv" } };
        _backups.Copies.Add(new BackupInfo(@"C:\data\backups\finance-2026-10-16_093000.db", new DateTime(2026, 10, 16, 9, 30, 0), 1_258_291));
        _backups.Copies.Add(new BackupInfo(@"C:\data\backups\finance-2026-10-15_090000.db", new DateTime(2026, 10, 15, 9, 0, 0), 2_048));
        _autostart.IsEnabled = true;

        var viewModel = Open();

        viewModel.HotKeyText.ShouldBe("Ctrl+Alt+M");
        viewModel.StartWithWindows.ShouldBeTrue();
        viewModel.ExportEnabled.ShouldBeTrue();
        viewModel.ExportPath.ShouldBe(@"C:\Notes\t.csv");
        viewModel.ExportStatusText.ShouldBe("Файл обновляется после каждого изменения.");
        viewModel.BackupsEnabled.ShouldBeTrue();
        viewModel.KeepText.ShouldBe("14");
        viewModel.BaseCurrencyText.ShouldBe("RUB");
        viewModel.Backups.Select(b => (b.Title, b.SizeText)).ShouldBe([("Сегодня, 09:30", "1,2 МБ"), ("15 октября 2026, 09:00", "2 КБ")]);
        _backend.Saves.ShouldBe(0); // loading changes nothing
        _autostart.Changes.ShouldBe(0);
    }

    [Fact]
    public async Task EnableExport_SavesAndWritesTheFileAtOnce()
    {
        var viewModel = Open();
        viewModel.ExportEnabled = true;
        viewModel.ExportProblem!.ShouldContain("Укажите файл");

        viewModel.ExportPath = "notes.csv";
        viewModel.ExportProblem!.ShouldContain("полный путь");
        viewModel.ExportPath = @"C:\Notes\transactions.txt";
        viewModel.ExportProblem!.ShouldContain(".csv");

        viewModel.ExportPath = @" C:\Notes\transactions.csv ";
        viewModel.CanApplyExport.ShouldBeTrue();
        await viewModel.ApplyExportCommand.ExecuteAsync(null);

        _backend.Settings.CsvExport.ShouldBe(new CsvExportSettings { Enabled = true, Path = @"C:\Notes\transactions.csv" });
        _backend.Exports.ShouldBe(1);
        viewModel.ExportStatusText.ShouldStartWith("Записано 13 строк в ");
        viewModel.ExportFailed.ShouldBeFalse();
    }

    [Fact]
    public async Task ExportError_IsShown()
    {
        _backend.ExportResult = new CsvExportStatus(CsvExportState.Failed, Error: "Не удалось записать CSV: диск только для чтения");
        var viewModel = Open();
        viewModel.ExportEnabled = true;
        viewModel.ExportPath = @"C:\Notes\t.csv";

        await viewModel.ApplyExportCommand.ExecuteAsync(null);

        viewModel.ExportFailed.ShouldBeTrue();
        viewModel.ExportStatusText.ShouldContain("только для чтения");
    }

    [Fact]
    public void TurningExportOff_AppliesAtOnce_KeepingThePath()
    {
        _backend.Settings = new AppSettings { CsvExport = new CsvExportSettings { Enabled = true, Path = @"C:\Notes\t.csv" } };
        var viewModel = Open();

        viewModel.ExportEnabled = false;

        _backend.Settings.CsvExport.ShouldBe(new CsvExportSettings { Enabled = false, Path = @"C:\Notes\t.csv" });
        _backend.Exports.ShouldBe(0);
        viewModel.ExportStatusText.ShouldBe("Экспорт выключен.");
    }

    [Fact]
    public void Backups_SwitchAndKeep_AreSaved_AndValidated()
    {
        var viewModel = Open();

        viewModel.KeepText = "30";
        _backend.Settings.Backups.Keep.ShouldBe(30);
        viewModel.BackupStatus.ShouldBe("Каждый день при запуске; хранятся последние 30.");

        viewModel.KeepText = "0";
        viewModel.BackupFailed.ShouldBeTrue();
        _backend.Settings.Backups.Keep.ShouldBe(30);

        viewModel.KeepText = "7";
        viewModel.BackupsEnabled = false;
        _backend.Settings.Backups.ShouldBe(new BackupSettings { Enabled = false, Keep = 7 });
        viewModel.BackupStatus.ShouldBe("Ежедневные копии выключены.");
    }

    [Fact]
    public async Task BackupNow_AddsACopy()
    {
        var viewModel = Open();
        viewModel.BackupStatus.ShouldBe("Копий пока нет.");

        await viewModel.BackupNowCommand.ExecuteAsync(null);

        viewModel.Backups.ShouldHaveSingleItem();
        viewModel.BackupStatus.ShouldBe("Копия сделана: finance-2026-10-16_120000.db");
    }

    [Fact]
    public async Task BackupError_IsShown()
    {
        _backups.Failure = new IOException("Диск заполнен");
        var viewModel = Open();

        await viewModel.BackupNowCommand.ExecuteAsync(null);

        viewModel.BackupFailed.ShouldBeTrue();
        viewModel.BackupStatus.ShouldBe("Не удалось сделать копию: Диск заполнен");
    }

    [Fact]
    public void Autostart_IsSwitched_AndAFailureShowsTheRealState()
    {
        var viewModel = Open();

        viewModel.StartWithWindows = true;
        _autostart.IsEnabled.ShouldBeTrue();

        _autostart.Fail = true;
        viewModel.StartWithWindows = false;
        viewModel.StartWithWindows.ShouldBeTrue();
    }

    [Fact]
    public void Folders_AndHotKey_AreRequestedFromTheView()
    {
        var viewModel = Open();
        string? opened = null;
        var hotKey = false;
        viewModel.OpenFolderRequested += (_, folder) => opened = folder;
        viewModel.ChangeHotKeyRequested += (_, _) => hotKey = true;

        viewModel.OpenDataFolderCommand.Execute(null);
        opened.ShouldBe(_backend.DataDirectory);
        viewModel.ChangeHotKeyCommand.Execute(null);
        hotKey.ShouldBeTrue();
    }

    private sealed class FakeBackend : ISettingsBackend
    {
        public AppSettings Settings { get; set; } = new();

        public int Saves { get; private set; }

        public int Exports { get; private set; }

        public CsvExportStatus ExportResult { get; set; } = new(CsvExportState.Succeeded, new DateTime(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc), 13);

        public string DataDirectory => @"C:\data";

        public CsvExportStatus ExportStatus => CsvExportStatus.Idle;

        public AppSettings Load() => Settings;

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            Saves++;
            Settings = settings;
            return Task.CompletedTask;
        }

        public Task<CsvExportStatus> ExportNowAsync(CancellationToken cancellationToken = default)
        {
            Exports++;
            return Task.FromResult(ExportResult);
        }
    }

    private sealed class FakeBackups : IDatabaseBackup
    {
        public List<BackupInfo> Copies { get; } = [];

        public Exception? Failure { get; set; }

        public string Folder => Path.Combine(Path.GetTempPath(), "FinanceApp.Tests", "backups-" + Guid.NewGuid().ToString("N"));

        public IReadOnlyList<BackupInfo> List() => Copies.OrderByDescending(c => c.CreatedLocal).ToList();

        public Task<BackupInfo> CreateAsync(CancellationToken cancellationToken = default)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            var copy = new BackupInfo(@"C:\data\backups\finance-2026-10-16_120000.db", new DateTime(2026, 10, 16, 12, 0, 0), 4096);
            Copies.Add(copy);
            return Task.FromResult(copy);
        }

        public Task<BackupInfo?> EnsureDailyAsync(CancellationToken cancellationToken = default) => Task.FromResult<BackupInfo?>(null);
    }

    private sealed class FakeAutostart : IAutostart
    {
        public bool IsEnabled { get; set; }

        public bool Fail { get; set; }

        public int Changes { get; private set; }

        public void SetEnabled(bool enabled)
        {
            if (Fail)
            {
                throw new UnauthorizedAccessException();
            }

            Changes++;
            IsEnabled = enabled;
        }
    }

    private sealed class Registrar : IHotKeyRegistrar
    {
        public HotKeyGesture? Current { get; } = new(HotKeyModifiers.Ctrl | HotKeyModifiers.Alt, "M");

        public bool TryRegister(HotKeyGesture gesture, out string? error)
        {
            error = null;
            return true;
        }
    }
}
