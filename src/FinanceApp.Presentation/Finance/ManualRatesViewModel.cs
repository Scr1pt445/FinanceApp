using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FinanceApp.Application.ExchangeRates;
using FinanceApp.Domain;

namespace FinanceApp.Presentation.Finance;

/// <summary>A line of «Свои курсы»: «1 EUR = 3,45 BYN», «30 октября — 5 ноября».</summary>
public sealed record ManualRateItem(ManualRate Rate, string Title, string PeriodText, bool IsExpired, bool IsActive);

/// <summary>A choice of how long a typed rate lasts.</summary>
public sealed record RatePeriodChoice(ManualRatePeriod Period, string Title)
{
    public static IReadOnlyList<RatePeriodChoice> All { get; } =
    [
        new(ManualRatePeriod.Day, "День"),
        new(ManualRatePeriod.Week, "Неделя"),
        new(ManualRatePeriod.Month, "Месяц"),
        new(ManualRatePeriod.UntilRemoved, "Пока не уберу"),
    ];
}

/// <summary>
/// «Курсы валют»: the user's own rates. Abroad or without access to the Central Bank, the totals still add up —
/// with a rate typed for a day, a week, a month or until removed. A typed rate wins over the CBR one.
/// </summary>
public sealed partial class ManualRatesViewModel(IFinanceBackend backend, TimeProvider timeProvider) : ObservableObject
{
    private int _hint;

    public ObservableCollection<ManualRateItem> Rates { get; } = [];

    /// <summary>The base currency, the usual ones and those of the accounts.</summary>
    public ObservableCollection<CurrencyCode> Currencies { get; } = [];

    public IReadOnlyList<RatePeriodChoice> Periods => RatePeriodChoice.All;

    public bool HasRates => Rates.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RateLabel), nameof(Problem), nameof(CanSave))]
    public partial CurrencyCode? From { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RateLabel), nameof(RateUnit), nameof(Problem), nameof(CanSave))]
    public partial CurrencyCode? To { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Problem), nameof(CanSave))]
    public partial string RateText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PeriodHint))]
    public partial RatePeriodChoice Period { get; set; } = RatePeriodChoice.All[1];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Problem), nameof(CanSave), nameof(PeriodHint))]
    public partial string StartText { get; set; } = string.Empty;

    /// <summary>«Сейчас по ЦБ: 1 EUR = 3,41 BYN» — what the typed rate replaces.</summary>
    [ObservableProperty]
    public partial string CurrentHint { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Status { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    public partial bool IsBusy { get; private set; }

    public string RateLabel => From is { } from ? $"1 {from} =" : string.Empty;

    public string RateUnit => To?.Value ?? string.Empty;

    /// <summary>«Действует 30 октября — 5 ноября».</summary>
    public string PeriodHint =>
        !Format.TryParseDate(StartText, out var start) ? string.Empty
        : ManualRate.EndOf(start, Period.Period) is { } end
            ? end == start ? $"Действует только {start.ToString("d MMMM yyyy", Format.Russian)}" : $"Действует {Format.Range(start, end)}"
            : $"Действует с {start.ToString("d MMMM yyyy", Format.Russian)}, пока не уберёте";

    public string? Problem =>
        From is null || To is null ? "Выберите обе валюты."
        : From == To ? "Валюты должны различаться."
        : !Format.TryParseAmount(RateText, out var rate) || rate <= 0 ? "Курс — положительное число."
        : !Format.TryParseDate(StartText, out _) ? "Выберите, с какого дня действует курс."
        : null;

    public bool CanSave => Problem is null && !IsBusy;

    /// <summary>A rate was added or removed: the totals have to be counted again.</summary>
    public event EventHandler? Changed;

    private DateOnly Today => DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var catalog = await backend.LoadCatalogAsync(cancellationToken);
            var choices = new[] { catalog.BaseCurrency, CurrencyCode.Usd, CurrencyCode.Eur, CurrencyCode.Rub }
                .Concat(catalog.Accounts.Where(a => !a.IsHidden).Select(a => a.Currency))
                .Distinct()
                .ToList();
            if (!Currencies.SequenceEqual(choices))
            {
                Currencies.Clear();
                foreach (var currency in choices)
                {
                    Currencies.Add(currency);
                }
            }

            To ??= catalog.BaseCurrency;
            From ??= choices.FirstOrDefault(c => c != To);
            if (StartText.Length == 0)
            {
                StartText = Format.DateText(Today);
            }

            await ReloadRatesAsync(cancellationToken);
            Status = string.Empty;
        }
#pragma warning disable CA1031 // Shown on the screen.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            Status = $"Не удалось загрузить курсы: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ChooseFrom(CurrencyCode currency) => From = currency;

    [RelayCommand]
    private void ChooseTo(CurrencyCode currency) => To = currency;

    [RelayCommand]
    private void ChoosePeriod(RatePeriodChoice period) => Period = period;

    /// <summary>«1 EUR = 3,4 BYN» ⇄ «1 BYN = 0,294 EUR».</summary>
    [RelayCommand]
    private void Swap()
    {
        (From, To) = (To, From);
        if (Format.TryParseAmount(RateText, out var rate) && rate > 0)
        {
            RateText = decimal.Round(1m / rate, 6).ToString("0.######", Format.Russian);
        }
    }

    [RelayCommand]
    private async Task AddAsync(CancellationToken cancellationToken)
    {
        if (!CanSave)
        {
            Status = Problem ?? string.Empty;
            return;
        }

        Format.TryParseAmount(RateText, out var rate);
        Format.TryParseDate(StartText, out var start);
        IsBusy = true;
        try
        {
            await backend.AddManualRateAsync(From!.Value, To!.Value, rate, start, Period.Period, cancellationToken);
            RateText = string.Empty;
            Status = string.Empty;
            await ReloadRatesAsync(cancellationToken);
            Changed?.Invoke(this, EventArgs.Empty);
        }
#pragma warning disable CA1031 // Shown on the screen.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            Status = $"Не удалось сохранить курс: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RemoveAsync(ManualRateItem item, CancellationToken cancellationToken)
    {
        try
        {
            await backend.RemoveManualRateAsync(item.Rate, cancellationToken);
            await ReloadRatesAsync(cancellationToken);
            Changed?.Invoke(this, EventArgs.Empty);
        }
#pragma warning disable CA1031 // Shown on the screen.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            Status = $"Не удалось убрать курс: {ex.Message}";
        }
    }

    partial void OnFromChanged(CurrencyCode? value) => _ = RefreshHintAsync();

    partial void OnToChanged(CurrencyCode? value) => _ = RefreshHintAsync();

    /// <summary>The rate in force today for the chosen pair, so the user sees what the typed one replaces.</summary>
    private async Task RefreshHintAsync()
    {
        var version = ++_hint;
        if (From is not { } from || To is not { } to || from == to)
        {
            CurrentHint = string.Empty;
            return;
        }

        string hint;
        try
        {
            var quote = await backend.GetRateAsync(from, to, Today);
            var source = quote.IsManual ? "по вашему курсу" : quote.IsFallback ? "по последнему известному курсу ЦБ" : "по ЦБ";
            hint = $"Сейчас {source}: 1 {from} = {quote.Rate.ToString("0.####", Format.Russian)} {to}";
        }
        catch (ExchangeRateUnavailableException)
        {
            hint = $"Курса {from} → {to} сейчас нет: ЦБ недоступен.";
        }
#pragma warning disable CA1031 // Only a hint.
        catch (Exception)
#pragma warning restore CA1031
        {
            hint = string.Empty;
        }

        if (version == _hint)
        {
            CurrentHint = hint;
        }
    }

    private async Task ReloadRatesAsync(CancellationToken cancellationToken)
    {
        var today = Today;
        var rates = await backend.ListManualRatesAsync(cancellationToken);
        Rates.Clear();
        foreach (var rate in rates.OrderBy(r => r.IsExpired(today)).ThenByDescending(r => r.ValidFrom))
        {
            Rates.Add(Describe(rate, today));
        }

        OnPropertyChanged(nameof(HasRates));
        await RefreshHintAsync();
    }

    public static ManualRateItem Describe(ManualRate rate, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(rate);
        var title = $"1 {rate.From} = {rate.Rate.ToString("0.######", Format.Russian)} {rate.To}";
        var period = rate.ValidTo is not { } end ? $"с {rate.ValidFrom.ToString("d MMMM yyyy", Format.Russian)}, пока не уберёте"
            : end == rate.ValidFrom ? $"только {rate.ValidFrom.ToString("d MMMM yyyy", Format.Russian)}"
            : Format.Range(rate.ValidFrom, end);
        var expired = rate.IsExpired(today);
        if (expired)
        {
            period += " · истёк";
        }
        else if (rate.ValidFrom > today)
        {
            period += " · ещё не начал действовать";
        }

        return new ManualRateItem(rate, title, period, expired, rate.IsActiveOn(today));
    }
}
