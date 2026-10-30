using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FinanceApp.Application.Setup;
using FinanceApp.Domain;
using FinanceApp.Presentation.Finance;

namespace FinanceApp.Presentation.Setup;

/// <summary>A page of the short tour: an icon, a heading, a few lines and examples to try.</summary>
public sealed record TourSlide(string Icon, string Title, string Text, IReadOnlyList<string> Examples)
{
    public bool HasExamples => Examples.Count > 0;
}

/// <summary>A currency offered as the base one: «RUB · Российский рубль».</summary>
public sealed record CurrencyOption(CurrencyCode Code, string Name);

/// <summary>What the person chose on the first run.</summary>
/// <param name="OpeningBalances">Starter account name → what is on it now; zero ones may be left out.</param>
/// <param name="Envelope">The first envelope, if one was named.</param>
/// <param name="QuickEntryLine">Show the one-line quick entry on the home screen.</param>
public sealed record SetupChoices(
    CurrencyCode BaseCurrency,
    IReadOnlyDictionary<string, decimal> OpeningBalances,
    string? Envelope = null,
    decimal? EnvelopeGoal = null,
    bool QuickEntryLine = false);

/// <summary>Applies the first-run choices: the base currency, the starter catalog and the balances.</summary>
public interface ISetupBackend
{
    Task CompleteAsync(SetupChoices choices, CancellationToken cancellationToken = default);
}

/// <summary>«Сколько сейчас на карте»: a starter account and the amount typed for it.</summary>
public sealed partial class OpeningBalanceItem(string account) : ObservableObject
{
    public string Account { get; } = account;

    [ObservableProperty]
    public partial string AmountText { get; set; } = string.Empty;
}

/// <summary>
/// The first run on a new phone: a four-page tour of what the app does, then the base currency, which cannot
/// change later, and what is on the starter accounts now — so the totals are right from the first entry.
/// The same tour can be opened again from «Ещё» without the setting up.
/// </summary>
public sealed partial class SetupViewModel : ObservableObject
{
    private readonly ISetupBackend? _backend;

    private SetupViewModel(ISetupBackend? backend)
    {
        _backend = backend;
        foreach (var account in DefaultCatalog.Accounts)
        {
            Balances.Add(new OpeningBalanceItem(account));
        }
    }

    public static IReadOnlyList<TourSlide> Slides { get; } =
    [
        new("👋", "Добро пожаловать",
            "FinanceApp — учёт денег, на который хватает пяти секунд. Всё хранится только на этом телефоне: без регистрации, без облака.",
            []),
        new("⚡", "Запись одной строкой",
            "Кому удобнее печатать, чем нажимать кнопки: сумма и пара слов — приложение поймёт, что это за операция, и покажет, как поняло, ещё до сохранения. Включается в конце настройки или в «Ещё → Настройки».",
            ["350 еда кофе — расход", "+50000 зарплата — доход", "1200 такси вчера — с датой", ">5000 отпуск — в конверт", "20 кофе @наличные — с другого счёта"]),
        new("💼", "Счета и конверты",
            "Счета — откуда вы платите: карта, наличные. Конверты — отдельные копилки на машину, отпуск, подушку. Поставьте цель, и приложение подскажет, сколько откладывать в месяц.",
            []),
        new("📊", "Итоги и ваши данные",
            "Куда ушли доходы, сколько осталось и как вы тратите по сравнению с прошлым месяцем. Операции можно выгрузить в CSV или сохранить копию базы через «Поделиться».",
            []),
    ];

    public static IReadOnlyList<CurrencyOption> Currencies { get; } =
    [
        new(CurrencyCode.Rub, "Российский рубль"),
        new(CurrencyCode.Usd, "Доллар США"),
        new(CurrencyCode.Eur, "Евро"),
        new(CurrencyCode.Parse("BYN"), "Белорусский рубль"),
        new(CurrencyCode.Parse("KZT"), "Казахстанский тенге"),
    ];

    /// <summary>Only the tour, from «Ещё»: nothing is set up or saved.</summary>
    public bool IsTourOnly => _backend is null;

    public int StepCount => IsTourOnly ? Slides.Count : Slides.Count + 2;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Slide), nameof(IsTour), nameof(IsCurrencyStep), nameof(IsBalancesStep), nameof(IsFirst), nameof(IsLast), nameof(NextText), nameof(Dots))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    public partial int Index { get; private set; }

    public TourSlide? Slide => IsTour ? Slides[Index] : null;

    public bool IsTour => Index < Slides.Count;

    public bool IsCurrencyStep => !IsTourOnly && Index == Slides.Count;

    public bool IsBalancesStep => !IsTourOnly && Index == Slides.Count + 1;

    public bool IsFirst => Index == 0;

    public bool IsLast => Index == StepCount - 1;

    public string NextText => !IsLast ? "Далее" : IsTourOnly ? "Понятно" : "Начать";

    /// <summary>The page indicator: one dot per step, the current one filled.</summary>
    public IReadOnlyList<bool> Dots => Enumerable.Range(0, StepCount).Select(i => i == Index).ToList();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrencySymbol))]
    public partial CurrencyOption BaseCurrency { get; set; } = Currencies[0];

    /// <summary>Shown next to the balances: «₽», «$»…</summary>
    public string CurrencySymbol => Format.Money(0, BaseCurrency.Code).Split(' ')[^1];

    public ObservableCollection<OpeningBalanceItem> Balances { get; } = [];

    [ObservableProperty]
    public partial string EnvelopeName { get; set; } = string.Empty;

    /// <summary>Off by default: buttons and forms suit most people; the line is for those who like to type.</summary>
    [ObservableProperty]
    public partial bool QuickEntryLine { get; set; }

    [ObservableProperty]
    public partial string EnvelopeGoalText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Problem { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    public partial bool IsBusy { get; private set; }

    /// <summary>Raised when the tour is over or the setup is saved: the app shows the tabs or closes the tour.</summary>
    public event EventHandler? Completed;

    public static SetupViewModel ForFirstRun(ISetupBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        return new SetupViewModel(backend);
    }

    public static SetupViewModel ForTour() => new(null);

    private bool CanGoBack => Index > 0;

    private bool CanGoNext => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private async Task NextAsync(CancellationToken cancellationToken)
    {
        if (!IsLast)
        {
            Index++;
            return;
        }

        if (IsTourOnly)
        {
            Completed?.Invoke(this, EventArgs.Empty);
            return;
        }

        await FinishAsync(cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back() => Index--;

    /// <summary>«Пропустить»: past the tour — on the first run straight to the setting up, which is needed anyway.</summary>
    [RelayCommand]
    private void Skip()
    {
        if (IsTourOnly)
        {
            Completed?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            Index = Slides.Count;
        }
    }

    [RelayCommand]
    private void ChooseCurrency(CurrencyOption option) => BaseCurrency = option;

    private async Task FinishAsync(CancellationToken cancellationToken)
    {
        var balances = new Dictionary<string, decimal>();
        foreach (var item in Balances)
        {
            if (item.AmountText.Trim().Length == 0)
            {
                continue;
            }

            if (!Format.TryParseAmount(item.AmountText, out var amount))
            {
                Problem = $"«{item.Account}»: сумма — число, например 15000 или 1250,50.";
                return;
            }

            balances[item.Account] = amount;
        }

        decimal? goal = null;
        if (EnvelopeGoalText.Trim().Length > 0)
        {
            if (!Format.TryParseAmount(EnvelopeGoalText, out var parsed) || parsed <= 0)
            {
                Problem = "Цель конверта — сумма больше нуля, или оставьте поле пустым.";
                return;
            }

            goal = parsed;
        }

        var envelope = EnvelopeName.Trim();
        IsBusy = true;
        Problem = string.Empty;
        try
        {
            await _backend!.CompleteAsync(new SetupChoices(BaseCurrency.Code, balances, envelope.Length > 0 ? envelope : null, envelope.Length > 0 ? goal : null, QuickEntryLine), cancellationToken);
            Completed?.Invoke(this, EventArgs.Empty);
        }
#pragma warning disable CA1031 // A full disk or a duplicate name is shown on the page; the choices stay for another try.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            Problem = $"Не получилось: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
