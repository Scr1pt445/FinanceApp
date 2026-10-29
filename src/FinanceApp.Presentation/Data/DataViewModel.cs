using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FinanceApp.Presentation.Finance;
using FinanceApp.Presentation.Settings;

namespace FinanceApp.Presentation.Data;

/// <summary>
/// «Данные» on the phone: the data never leaves it by itself, so CSV and copies of the database are handed
/// to other apps — mail, a messenger, a cloud drive — through sharing (ADR 0013). The CSV can be imported
/// into the desktop app; a copy restores everything.
/// </summary>
public sealed partial class DataViewModel(IDataBackend backend, IFileSharer sharer, TimeProvider timeProvider) : ObservableObject
{
    public ObservableCollection<BackupRow> Backups { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ShareCsvCommand), nameof(ShareBackupCommand))]
    public partial bool IsBusy { get; private set; }

    /// <summary>What the last action did, or why it failed.</summary>
    [ObservableProperty]
    public partial string Status { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool Failed { get; private set; }

    /// <summary>«Копий: 3, последняя — сегодня, 09:30» or «Копий пока нет».</summary>
    [ObservableProperty]
    public partial string BackupsText { get; private set; } = string.Empty;

    private bool CanAct => !IsBusy;

    public void Load()
    {
        Backups.Clear();
        var today = timeProvider.GetLocalNow().Date;
        foreach (var copy in backend.ListBackups())
        {
            Backups.Add(BackupRow.From(copy, today));
        }

        BackupsText = Backups.Count == 0
            ? "Копий пока нет. Приложение делает одну в день при запуске."
            : $"Копий на телефоне: {Backups.Count}, последняя — {Backups[0].Title.ToLower(Format.Russian)}.";
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task ShareCsvAsync(CancellationToken cancellationToken) => RunAsync(async () =>
    {
        var exported = await backend.ExportCsvAsync(sharer.ShareFolder, cancellationToken);
        await sharer.ShareAsync("FinanceApp — операции в CSV", exported.Paths);
        return $"Выгружено операций: {exported.Rows}.";
    });

    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task ShareBackupAsync(CancellationToken cancellationToken) => RunAsync(async () =>
    {
        var copy = await backend.BackupAsync(cancellationToken);
        Load();
        await sharer.ShareAsync("FinanceApp — копия базы", [copy.Path]);
        return $"Копия сделана: {Path.GetFileName(copy.Path)}, {Format.FileSize(copy.SizeBytes)}.";
    });

    private async Task RunAsync(Func<Task<string>> action)
    {
        IsBusy = true;
        try
        {
            Status = await action();
            Failed = false;
        }
#pragma warning disable CA1031 // A full disk or a closed share sheet must not take the app down; say what happened.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            Status = $"Не получилось: {ex.Message}";
            Failed = true;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
