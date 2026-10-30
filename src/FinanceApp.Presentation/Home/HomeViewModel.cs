using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FinanceApp.Application.Transactions;
using FinanceApp.Domain;
using FinanceApp.Presentation.Finance;
using FinanceApp.Presentation.QuickEntry;

namespace FinanceApp.Presentation.Home;

/// <summary>
/// The phone's first screen: the quick-entry line, the total balance and the latest operations by day.
/// The screen stays open between entries, so after a save the line is ready for the next one at once,
/// and the total and the list catch up in the background.
/// </summary>
public sealed partial class HomeViewModel : ObservableObject
{
    /// <summary>How far back the list goes; the full history is the operations screen's job.</summary>
    public const int RecentDays = 30;

    /// <summary>Enough to scroll through on a phone without loading a busy month in full.</summary>
    public const int RecentLimit = 50;

    private readonly IFinanceBackend _backend;
    private readonly TimeProvider _timeProvider;
    private CurrencyCode _baseCurrency = CurrencyCode.Rub;

    public HomeViewModel(IFinanceBackend backend, QuickEntryViewModel quickEntry, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(quickEntry);
        _backend = backend;
        _timeProvider = timeProvider;
        QuickEntry = quickEntry;
        QuickEntry.Saved += (_, saved) => _ = OnSavedAsync(saved);
    }

    public QuickEntryViewModel QuickEntry { get; }

    /// <summary>Everything on accounts and in envelopes, e.g. «48 650 ₽».</summary>
    [ObservableProperty]
    public partial string TotalText { get; private set; } = string.Empty;

    /// <summary>«Счета 45 650 ₽ · Конверты 3 000 ₽».</summary>
    [ObservableProperty]
    public partial string TotalDetails { get; private set; } = string.Empty;

    /// <summary>Missing rates or a failed load; empty when all is well.</summary>
    [ObservableProperty]
    public partial string Warning { get; private set; } = string.Empty;

    /// <summary>What the last save recorded, e.g. «Сохранено: Расход 350 ₽».</summary>
    [ObservableProperty]
    public partial string LastSaved { get; private set; } = string.Empty;

    public ObservableCollection<DayGroup> Recent { get; } = [];

    [ObservableProperty]
    public partial bool IsEmpty { get; private set; }

    private DateOnly Today => DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);

    /// <summary>Called when the screen appears: a clean line, fresh totals and list.</summary>
    public async Task OpenAsync(CancellationToken cancellationToken = default)
    {
        await QuickEntry.OpenAsync(cancellationToken);
        await RefreshAsync(cancellationToken);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var balances = await _backend.LoadBalancesAsync(cancellationToken: cancellationToken);
            _baseCurrency = balances.BaseCurrency;
            var display = balances.DisplayCurrency;
            TotalText = Format.Balance(balances.InDisplay(balances.Total), display);
            TotalDetails = $"Счета {Format.Balance(balances.InDisplay(balances.FreeTotal), display)} · Конверты {Format.Balance(balances.InDisplay(balances.EnvelopesTotal), display)}";

            var today = Today;
            var overview = await _backend.LoadPeriodAsync(Period.Custom(today.AddDays(1 - RecentDays), today), cancellationToken);
            var accounts = overview.Accounts.ToDictionary(a => a.Id);
            var categories = overview.Categories.ToDictionary(c => c.Id);
            var rows = overview.Transactions
                .Take(RecentLimit)
                .Select(t => OperationRow.Create(t, viewed: null, accounts, categories, overview.BaseCurrency));

            Recent.Clear();
            foreach (var day in DayGroup.Build(rows, viewed: null, overview.BaseCurrency, today))
            {
                Recent.Add(day);
            }

            IsEmpty = Recent.Count == 0;
            Warning = balances.HasMissingRates ? "Курс ЦБ для части валют ещё не загружен — они пока не в итоге." : string.Empty;
        }
#pragma warning disable CA1031 // Shown on the screen; quick entry stays usable.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            Warning = $"Не удалось загрузить данные: {ex.Message}";
        }
    }

    private async Task OnSavedAsync(SavedTransaction saved)
    {
        LastSaved = "Сохранено: " + QuickEntryFormatter.Saved(saved, _baseCurrency);

        // The new entry changes hints and default accounts for the next line.
        await QuickEntry.ReloadContextAsync();
        await RefreshAsync();
    }
}
