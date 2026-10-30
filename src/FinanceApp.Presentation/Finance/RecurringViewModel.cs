using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FinanceApp.Application.Accounts;
using FinanceApp.Application.Recurring;
using FinanceApp.Domain;

namespace FinanceApp.Presentation.Finance;

/// <summary>A choice of how often an operation repeats; <c>null</c> — it does not.</summary>
public sealed record RepeatChoice(RecurrenceFrequency? Frequency, string Title)
{
    public static IReadOnlyList<RepeatChoice> All { get; } =
    [
        new(null, "Не повторять"),
        new(RecurrenceFrequency.Weekly, "Каждую неделю"),
        new(RecurrenceFrequency.Monthly, "Каждый месяц"),
        new(RecurrenceFrequency.Yearly, "Каждый год"),
    ];

    public static IReadOnlyList<RepeatChoice> Frequencies { get; } = All.Skip(1).ToList();
}

/// <summary>How a recurring operation reads: «Аренда», «30 000 ₽ · Карта», «каждый месяц, 5-го», «5 ноября».</summary>
public static class RecurringText
{
    public static string Title(RecurringOperation operation, Catalog catalog)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(catalog);
        if (operation.Note is { } note)
        {
            return note;
        }

        return operation.Type == TransactionType.Transfer
            ? $"Перевод в «{catalog.Accounts.FirstOrDefault(a => a.Id == operation.ToAccountId)?.Name}»"
            : catalog.Categories.FirstOrDefault(c => c.Id == operation.CategoryId)?.Name ?? "Операция";
    }

    public static string Amount(RecurringOperation operation, Catalog catalog)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(catalog);
        var account = catalog.Accounts.FirstOrDefault(a => a.Id == operation.AccountId);
        var money = Format.Money(operation.Amount, account?.Currency ?? catalog.BaseCurrency);
        var sign = operation.Type switch
        {
            TransactionType.Income => "+",
            TransactionType.Expense => "−",
            _ => string.Empty,
        };
        return $"{sign}{money} · {account?.Name}";
    }

    public static string Schedule(RecurringOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return operation.Frequency switch
        {
            RecurrenceFrequency.Weekly => $"каждую неделю, {operation.NextDate.ToString("dddd", Format.Russian)}",
            RecurrenceFrequency.Yearly => $"каждый год, {operation.NextDate.ToString("d MMMM", Format.Russian)}",
            _ => $"каждый месяц, {operation.Anchor}-го",
        };
    }

    /// <summary>«сегодня», «вчера», «5 ноября».</summary>
    public static string When(DateOnly date, DateOnly today) =>
        (today.DayNumber - date.DayNumber) switch
        {
            0 => "сегодня",
            1 => "вчера",
            -1 => "завтра",
            _ => date.ToString(date.Year == today.Year ? "d MMMM" : "d MMMM yyyy", Format.Russian),
        };
}

/// <summary>
/// A recurring operation that is due, on the home screen: «Аренда — 30 000 ₽ · Карта, сегодня» with «Записать»
/// (the amount may be changed first) and «Пропустить».
/// </summary>
public sealed partial class DueItem(RecurringOperation operation, string title, string details, Func<DueItem, Task> record, Func<DueItem, Task> skip)
    : ObservableObject
{
    public RecurringOperation Operation { get; } = operation;

    public string Title { get; } = title;

    public string Details { get; } = details;

    public bool IsIncome => Operation.Type == TransactionType.Income;

    /// <summary>Prefilled with the usual amount; a bill that came out different is corrected here.</summary>
    [ObservableProperty]
    public partial string AmountText { get; set; } = operation.Amount.ToString("0.##", Format.Russian);

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [RelayCommand]
    private Task RecordAsync() => record(this);

    [RelayCommand]
    private Task SkipAsync() => skip(this);
}

/// <summary>A line of «Регулярные операции»: what, how much, how often, when next; opens the editor.</summary>
public sealed record RecurringItem(RecurringOperation Operation, string Title, string AmountText, string ScheduleText, string NextText, bool IsDue);

/// <summary>
/// «Ещё → Регулярные операции»: everything that comes round, the soonest first; a tap edits the amount, the
/// schedule or the next date, or deletes it. New ones come from the operation form («Повторять»).
/// </summary>
public sealed partial class RecurringViewModel(IFinanceBackend backend, TimeProvider timeProvider) : ObservableObject
{
    private Catalog? _catalog;

    public ObservableCollection<RecurringItem> Items { get; } = [];

    public bool IsEmpty => Items.Count == 0;

    [ObservableProperty]
    public partial string Status { get; private set; } = string.Empty;

    /// <summary>The open editor, or <c>null</c>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing))]
    public partial RecurringEditor? Editor { get; private set; }

    public bool IsEditing => Editor is not null;

    /// <summary>Something was added, changed or deleted: the home screen's due list is stale.</summary>
    public event EventHandler? Changed;

    private DateOnly Today => DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _catalog = await backend.LoadCatalogAsync(cancellationToken);
            var today = Today;
            Items.Clear();
            foreach (var operation in await backend.ListRecurringAsync(cancellationToken))
            {
                Items.Add(new RecurringItem(
                    operation,
                    RecurringText.Title(operation, _catalog),
                    RecurringText.Amount(operation, _catalog),
                    RecurringText.Schedule(operation),
                    operation.IsDue(today) ? $"пора записать — {RecurringText.When(operation.NextDate, today)}" : $"следующая — {RecurringText.When(operation.NextDate, today)}",
                    operation.IsDue(today)));
            }

            OnPropertyChanged(nameof(IsEmpty));
            Status = string.Empty;
        }
#pragma warning disable CA1031 // Shown on the screen.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            Status = $"Не удалось загрузить: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Edit(RecurringItem item)
    {
        var editor = new RecurringEditor(item.Operation, item.Title);
        editor.Closed += async (_, action) =>
        {
            Editor = null;
            await ApplyAsync(item.Operation, editor, action);
        };
        Editor = editor;
    }

    private async Task ApplyAsync(RecurringOperation operation, RecurringEditor editor, RecurringEditor.Action action)
    {
        try
        {
            switch (action)
            {
                case RecurringEditor.Action.Save:
                    Format.TryParseAmount(editor.AmountText, out var amount);
                    Format.TryParseDate(editor.NextText, out var next);
                    await backend.UpdateRecurringAsync(operation.Id, new RecurringInput(
                        operation.Type, amount, operation.CategoryId, operation.AccountId, operation.ToAccountId,
                        editor.Note, editor.Frequency.Frequency!.Value, next));
                    break;
                case RecurringEditor.Action.Delete:
                    await backend.DeleteRecurringAsync(operation.Id);
                    break;
                default:
                    return;
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }
#pragma warning disable CA1031 // Shown on the screen.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            Status = ex.Message;
        }

        await LoadAsync();
    }
}

/// <summary>Changing a recurring operation: the amount, the note, how often and the next date.</summary>
public sealed partial class RecurringEditor : ObservableObject
{
    public RecurringEditor(RecurringOperation operation, string title)
    {
        ArgumentNullException.ThrowIfNull(operation);
        Title = title;
        AmountText = operation.Amount.ToString("0.##", Format.Russian);
        Note = operation.Note ?? string.Empty;
        Frequency = RepeatChoice.Frequencies.First(f => f.Frequency == operation.Frequency);
        NextText = Format.DateText(operation.NextDate);
    }

    public enum Action
    {
        Cancel,
        Save,
        Delete,
    }

    public string Title { get; }

    public IReadOnlyList<RepeatChoice> Frequencies => RepeatChoice.Frequencies;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Problem))]
    public partial string AmountText { get; set; }

    [ObservableProperty]
    public partial string Note { get; set; }

    [ObservableProperty]
    public partial RepeatChoice Frequency { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Problem))]
    public partial string NextText { get; set; }

    public string? Problem =>
        !Format.TryParseAmount(AmountText, out var amount) || amount <= 0 || decimal.Round(amount, 2) != amount ? "Сумма — больше нуля, не больше двух знаков после запятой."
        : !Format.TryParseDate(NextText, out _) ? "Выберите дату следующей операции."
        : null;

    public event EventHandler<Action>? Closed;

    [RelayCommand]
    private void ChooseFrequency(RepeatChoice choice) => Frequency = choice;

    [RelayCommand]
    private void Save()
    {
        if (Problem is null)
        {
            Closed?.Invoke(this, Action.Save);
        }
    }

    [RelayCommand]
    private void Delete() => Closed?.Invoke(this, Action.Delete);

    [RelayCommand]
    private void Cancel() => Closed?.Invoke(this, Action.Cancel);
}
