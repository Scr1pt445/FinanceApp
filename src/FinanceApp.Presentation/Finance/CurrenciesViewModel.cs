using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FinanceApp.Application.Accounts;
using FinanceApp.Application.Settings;
using FinanceApp.Domain;
using FinanceApp.Presentation.QuickEntry;

namespace FinanceApp.Presentation.Finance;

/// <summary>
/// A currency in «Валюты»: «BYN · Br», «Белорусский рубль», why it cannot be removed («основная», «есть счёт
/// «Карта»»), or nothing when it can.
/// </summary>
public sealed record CurrencyRow(CurrencyCode Code, string Title, string Name, string Note, bool CanRemove);

/// <summary>
/// «Ещё → Валюты»: which currencies the app offers in its choices — rates, debts, the receipt currency, the totals.
/// The base currency and those of the accounts are always there; the rest the user adds and removes.
/// </summary>
public sealed partial class CurrenciesViewModel(IFinanceBackend backend) : ObservableObject
{
    private Catalog? _catalog;

    public ObservableCollection<CurrencyRow> Chosen { get; } = [];

    /// <summary>Known currencies not in the list yet, filtered by <see cref="Search"/>.</summary>
    public ObservableCollection<CurrencyRow> Available { get; } = [];

    /// <summary>A code or a part of the name: «тенге», «KZT».</summary>
    [ObservableProperty]
    public partial string Search { get; set; } = string.Empty;

    /// <summary>For the «Ещё» menu: «RUB · USD · BYN».</summary>
    [ObservableProperty]
    public partial string Summary { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Status { get; private set; } = string.Empty;

    /// <summary>The list changed: the totals' currency choices are stale.</summary>
    public event EventHandler? Changed;

    /// <summary>Search for an unknown code («XAU») offers to add it as it is.</summary>
    public CurrencyCode? CustomCode =>
        CurrencyCode.TryParse(Search, out var code) && CurrencyNames.Known.All(k => k.Code != code) && !Chosen.Any(c => c.Code == code)
            ? code
            : null;

    public bool HasCustomCode => CustomCode is not null;

    public string CustomTitle => CustomCode is { } code ? $"+ Добавить {code}" : string.Empty;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _catalog = await backend.LoadCatalogAsync(cancellationToken);
            Apply();
            Status = string.Empty;
        }
#pragma warning disable CA1031 // Shown on the screen.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            Status = $"Не удалось загрузить валюты: {ex.Message}";
        }
    }

    [RelayCommand]
    private Task AddAsync(CurrencyRow row) => SaveAsync(Mine().Append(row.Code).ToList());

    [RelayCommand]
    private Task AddCustomAsync() => CustomCode is { } code ? SaveAsync(Mine().Append(code).ToList()) : Task.CompletedTask;

    [RelayCommand]
    private Task RemoveAsync(CurrencyRow row) => row.CanRemove ? SaveAsync(Mine().Where(c => c != row.Code).ToList()) : Task.CompletedTask;

    partial void OnSearchChanged(string value)
    {
        FillAvailable();
        OnPropertyChanged(nameof(CustomCode));
        OnPropertyChanged(nameof(HasCustomCode));
        OnPropertyChanged(nameof(CustomTitle));
    }

    /// <summary>The user's own list as it is now: what was chosen, or the defaults if never changed.</summary>
    private IReadOnlyList<CurrencyCode> Mine() => _catalog?.ChosenCurrencies ?? CurrencyList.Defaults;

    private async Task SaveAsync(IReadOnlyList<CurrencyCode> chosen)
    {
        try
        {
            await backend.SetCurrenciesAsync(chosen);
            Search = string.Empty;
            await LoadAsync();
            Changed?.Invoke(this, EventArgs.Empty);
        }
#pragma warning disable CA1031 // Shown on the screen.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            Status = $"Не удалось сохранить: {ex.Message}";
        }
    }

    private void Apply()
    {
        var catalog = _catalog!;
        Chosen.Clear();
        foreach (var code in catalog.Currencies)
        {
            var account = catalog.Accounts.FirstOrDefault(a => !a.IsHidden && a.Currency == code);
            var note = code == catalog.BaseCurrency ? "основная — в ней считаются итоги"
                : account is not null ? $"есть счёт «{account.Name}»"
                : string.Empty;
            Chosen.Add(Row(code, note, canRemove: note.Length == 0));
        }

        Summary = string.Join(" · ", catalog.Currencies.Select(c => c.Value));
        FillAvailable();
        OnPropertyChanged(nameof(CustomCode));
        OnPropertyChanged(nameof(HasCustomCode));
        OnPropertyChanged(nameof(CustomTitle));
    }

    private void FillAvailable()
    {
        Available.Clear();
        var query = Search.Trim();
        foreach (var (code, name) in CurrencyNames.Known)
        {
            if (Chosen.Any(c => c.Code == code))
            {
                continue;
            }

            if (query.Length == 0
                || code.Value.Contains(query, StringComparison.OrdinalIgnoreCase)
                || name.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            {
                Available.Add(Row(code, string.Empty, canRemove: false));
            }
        }
    }

    private static CurrencyRow Row(CurrencyCode code, string note, bool canRemove)
    {
        var symbol = QuickEntryFormatter.Symbol(code);
        var title = symbol == code.Value ? code.Value : $"{code.Value} · {symbol}";
        return new CurrencyRow(code, title, CurrencyNames.Of(code), note, canRemove);
    }
}
