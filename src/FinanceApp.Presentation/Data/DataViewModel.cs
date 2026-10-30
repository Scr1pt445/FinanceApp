using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FinanceApp.Application.Backups;
using FinanceApp.Presentation.Finance;
using FinanceApp.Presentation.Settings;

namespace FinanceApp.Presentation.Data;

/// <summary>
/// «Данные» on the phone: the data never leaves it by itself, so CSV and copies of the database are handed
/// to other apps — mail, a messenger, a cloud drive — through sharing (ADR 0013). The CSV can be imported
/// into the desktop app; a copy restores everything.
/// </summary>
public sealed partial class DataViewModel(IDataBackend backend, IFileSharer sharer, TimeProvider timeProvider, IFileChooser? picker = null) : ObservableObject
{
    private string? _pendingPath;

    public ObservableCollection<BackupRow> Backups { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ShareCsvCommand), nameof(ShareBackupCommand), nameof(PickRestoreCommand), nameof(ConfirmRestoreCommand))]
    public partial bool IsBusy { get; private set; }

    /// <summary>What the last action did, or why it failed.</summary>
    [ObservableProperty]
    public partial string Status { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool Failed { get; private set; }

    /// <summary>«Копий: 3, последняя — сегодня, 09:30» or «Копий пока нет».</summary>
    [ObservableProperty]
    public partial string BackupsText { get; private set; } = string.Empty;

    /// <summary>A copy was chosen and checked: what it holds, before it replaces the data. Empty otherwise.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirmingRestore))]
    public partial string PendingRestore { get; private set; } = string.Empty;

    public bool IsConfirmingRestore => PendingRestore.Length > 0;

    public bool CanRestore => picker is not null;

    /// <summary>The data was replaced by a copy: every screen has to load again.</summary>
    public event EventHandler<RestoreResult>? Restored;

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

    /// <summary>«Восстановить из копии»: choose the file, see what it holds, then confirm.</summary>
    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task PickRestoreAsync(CancellationToken cancellationToken) => RunAsync(async () =>
    {
        PendingRestore = string.Empty;
        if (picker is null || await picker.PickAsync("Копия базы FinanceApp (.db)") is not { } path)
        {
            return string.Empty;
        }

        var contents = await backend.InspectBackupAsync(path, cancellationToken);
        _pendingPath = path;
        PendingRestore = $"{Describe(contents)} Данные на телефоне заменятся этой копией; нынешние сохранятся в копиях на телефоне.";
        return string.Empty;
    });

    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task ConfirmRestoreAsync(CancellationToken cancellationToken) => RunAsync(async () =>
    {
        if (_pendingPath is not { } path)
        {
            return string.Empty;
        }

        var result = await backend.RestoreAsync(path, cancellationToken);
        _pendingPath = null;
        PendingRestore = string.Empty;
        Load();
        Restored?.Invoke(this, result);
        return $"Данные восстановлены: операций {result.Contents.Transactions}, счетов {result.Contents.Accounts}. Основная валюта — {result.BaseCurrency}.";
    });

    [RelayCommand]
    private void CancelRestore()
    {
        _pendingPath = null;
        PendingRestore = string.Empty;
    }

    /// <summary>«В копии 1 234 операции и 5 счетов, последняя операция — 28 октября 2026.»</summary>
    public static string Describe(BackupContents contents)
    {
        ArgumentNullException.ThrowIfNull(contents);
        var last = contents.LastOperation is { } date ? $", последняя операция — {date.ToString("d MMMM yyyy", Format.Russian)}" : string.Empty;
        return $"В копии операций: {contents.Transactions}, счетов: {contents.Accounts}{last}.";
    }

    private async Task RunAsync(Func<Task<string>> action)
    {
        IsBusy = true;
        try
        {
            var status = await action();
            if (status.Length > 0)
            {
                Status = status;
            }

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
