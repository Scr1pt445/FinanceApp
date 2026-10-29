using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FinanceApp.Application.QuickEntry;
using FinanceApp.Application.Transactions;
using FinanceApp.Domain;

namespace FinanceApp.Presentation.QuickEntry;

/// <summary>
/// The quick-entry window: one input line, a live preview of how it is understood, Enter to save.
/// The context (catalogs, hints, default accounts) is loaded when the window opens; while it loads,
/// the previous one keeps the preview instant.
/// </summary>
public sealed partial class QuickEntryViewModel(IQuickEntryBackend backend) : ObservableObject
{
    public const string Hint = "350 еда кофе · +50000 зарплата · >10000 копилка · вчера · @нал";

    private QuickEntryContext? _context;
    private QuickEntryDraft? _draft;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string Input { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial bool IsBusy { get; set; }

    /// <summary>Type and amount, e.g. «Расход 350 ₽». Empty while there is nothing to show.</summary>
    [ObservableProperty]
    public partial string Headline { get; set; } = string.Empty;

    /// <summary>Category, account, date and note, e.g. «Еда · Наличные · вчера · кофе».</summary>
    [ObservableProperty]
    public partial string Details { get; set; } = string.Empty;

    /// <summary>A hint, the first problem with the line, or an error from saving.</summary>
    [ObservableProperty]
    public partial string Message { get; set; } = Hint;

    [ObservableProperty]
    public partial bool HasProblem { get; set; }

    /// <summary>Raised after a successful save; the window hides itself.</summary>
    public event EventHandler<SavedTransaction>? Saved;

    /// <summary>Raised on Esc; the window hides itself.</summary>
    public event EventHandler? CloseRequested;

    public bool CanSave => !IsBusy && _draft?.IsComplete == true;

    /// <summary>Shown under a complete line. The desktop window speaks of keys; a phone sets its own text.</summary>
    public string ReadyMessage { get; init; } = "Enter — сохранить, Esc — закрыть";

    /// <summary>Called every time the window is shown: clears the line and refreshes the context.</summary>
    public async Task OpenAsync(CancellationToken cancellationToken = default)
    {
        Input = string.Empty;
        UpdatePreview();
        await ReloadContextAsync(cancellationToken);
    }

    /// <summary>
    /// Fresh catalogs, hints and default accounts without touching the line — for a screen that stays open
    /// between entries, after something was saved.
    /// </summary>
    public async Task ReloadContextAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _context = await backend.LoadContextAsync(cancellationToken);
        }
#pragma warning disable CA1031 // The window must stay usable; the error is shown in place.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            ShowProblem($"Не удалось загрузить справочники: {ex.Message}");
            return;
        }

        UpdatePreview();
    }

    partial void OnInputChanged(string value) => UpdatePreview();

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            var saved = await backend.SaveAsync(Input, cancellationToken);
            Input = string.Empty;
            Saved?.Invoke(this, saved);
        }
        catch (QuickEntryException ex)
        {
            ShowProblem(ex.Issues.Count > 0 ? ex.Issues[0].Message : ex.Message);
        }
#pragma warning disable CA1031 // Rates offline, database locked…: show it, keep the line for another try.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            ShowProblem($"Не сохранено: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void UpdatePreview()
    {
        if (string.IsNullOrWhiteSpace(Input) || _context is null)
        {
            _draft = null;
            Headline = string.Empty;
            Details = string.Empty;
            Message = _context is null && !string.IsNullOrWhiteSpace(Input) ? "Загрузка справочников…" : Hint;
            HasProblem = false;
            SaveCommand.NotifyCanExecuteChanged();
            return;
        }

        var draft = QuickEntryService.Preview(Input, _context);
        _draft = draft;

        var isTransfer = draft.Type == TransactionType.Transfer;
        var typeName = isTransfer ? "В конверт" : QuickEntryFormatter.Type(draft.Type);
        Headline = draft.Amount is { } amount && draft.Account is { } account
            ? $"{typeName} {QuickEntryFormatter.Money(amount, account.Currency)}"
            : typeName;

        var parts = new List<string>
        {
            isTransfer
                ? "→ " + (draft.ToAccount?.Name ?? "конверт?")
                : draft.Category is null ? "категория?" : draft.Category.Name + (draft.CategoryFromHint ? " (по прошлым записям)" : string.Empty),
            (isTransfer ? "с " : string.Empty) + (draft.Account?.Name ?? "счёт?"),
            QuickEntryFormatter.Date(draft.Date, _context.Today),
        };
        if (draft.Note is not null)
        {
            parts.Add($"«{draft.Note}»");
        }

        Details = string.Join(" · ", parts);

        if (draft.Issues.Count > 0)
        {
            ShowProblem(draft.Issues[0].Message);
        }
        else
        {
            Message = ReadyMessage;
            HasProblem = false;
        }

        SaveCommand.NotifyCanExecuteChanged();
    }

    private void ShowProblem(string message)
    {
        Message = message;
        HasProblem = true;
    }
}
