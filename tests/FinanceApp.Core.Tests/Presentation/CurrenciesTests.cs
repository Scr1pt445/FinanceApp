using FinanceApp.Application.Settings;
using FinanceApp.Domain;
using FinanceApp.Presentation.Finance;
using FinanceApp.Presentation.QuickEntry;

namespace FinanceApp.Core.Tests.Presentation;

public class CurrenciesTests : FinanceViewModelTestBase
{
    private static readonly CurrencyCode Kzt = CurrencyCode.Parse("KZT");

    private async Task<CurrenciesViewModel> ScreenAsync()
    {
        var screen = new CurrenciesViewModel(Backend);
        await screen.LoadAsync(Ct);
        return screen;
    }

    [Fact]
    public void Resolve_BaseFirst_ThenChosen_ThenAccounts_WithoutHiddenOnes()
    {
        var usd = new Account(Guid.NewGuid(), "Доллары", CurrencyCode.Usd);
        var hidden = new Account(Guid.NewGuid(), "Старая", Kzt);
        hidden.Hide();

        CurrencyList.Resolve(CurrencyCode.Byn, null, [usd]).ShouldBe([CurrencyCode.Byn, CurrencyCode.Rub, CurrencyCode.Usd, CurrencyCode.Eur]);
        CurrencyList.Resolve(CurrencyCode.Byn, [], [usd, hidden]).ShouldBe([CurrencyCode.Byn, CurrencyCode.Usd]);
    }

    [Fact]
    public void Settings_CompareTheListByValue()
    {
        var a = new AppSettings { Currencies = [CurrencyCode.Byn] };

        a.ShouldBe(new AppSettings { Currencies = [CurrencyCode.Byn] });
        a.ShouldNotBe(new AppSettings());
        new AppSettings().ShouldBe(new AppSettings());
    }

    [Fact]
    public void Symbols_IncludeTheBelarusianRuble_AndPlainTextKeepsIt()
    {
        QuickEntryFormatter.Symbol(CurrencyCode.Byn).ShouldBe(QuickEntryFormatter.BynSign);
        QuickEntryFormatter.Symbol(Kzt).ShouldBe("₸");
        QuickEntryFormatter.Symbol(CurrencyCode.Parse("XAU")).ShouldBe("XAU");
        QuickEntryFormatter.Plain(Format.Money(1500m, CurrencyCode.Byn)).ShouldEndWith(" Br");
    }

    [Fact]
    public async Task Screen_ByDefault_RublesDollarsEuros_TheBaseOneStays()
    {
        var screen = await ScreenAsync();

        screen.Chosen.Select(c => c.Code).ShouldBe([CurrencyCode.Rub, CurrencyCode.Usd, CurrencyCode.Eur]);
        screen.Chosen[0].CanRemove.ShouldBeFalse();
        screen.Chosen[0].Note.ShouldStartWith("основная");
        screen.Chosen[1].Title.ShouldBe("USD · $");
        screen.Summary.ShouldBe("RUB · USD · EUR");
        screen.Available.ShouldNotContain(r => r.Code == CurrencyCode.Usd);
        screen.Available.ShouldContain(r => r.Code == CurrencyCode.Byn && r.Name == "Белорусский рубль");
    }

    [Fact]
    public async Task Screen_RemovesEuro_AddsTenge_AndTheFormOffersThem()
    {
        var screen = await ScreenAsync();
        var changed = 0;
        screen.Changed += (_, _) => changed++;

        await screen.RemoveCommand.ExecuteAsync(screen.Chosen.Single(c => c.Code == CurrencyCode.Eur));
        screen.Search = "тенге";
        screen.Available.ShouldHaveSingleItem().Code.ShouldBe(Kzt);
        await screen.AddCommand.ExecuteAsync(screen.Available.Single());

        Backend.Currencies.ShouldBe([CurrencyCode.Rub, CurrencyCode.Usd, Kzt]);
        screen.Chosen.Select(c => c.Code).ShouldBe([CurrencyCode.Rub, CurrencyCode.Usd, Kzt]);
        screen.Search.ShouldBeEmpty();
        changed.ShouldBe(2);

        var rates = new ManualRatesViewModel(Backend, Time);
        await rates.LoadAsync(Ct);
        rates.Currencies.ShouldBe([CurrencyCode.Rub, CurrencyCode.Usd, Kzt]);
    }

    [Fact]
    public async Task Screen_AnAccountsCurrency_CannotBeRemoved()
    {
        Backend.Add(new Account(Guid.NewGuid(), "Карта BYN", CurrencyCode.Byn, 7));
        await Backend.SetCurrenciesAsync([], Ct);
        var screen = await ScreenAsync();

        var byn = screen.Chosen.Single(c => c.Code == CurrencyCode.Byn);
        byn.CanRemove.ShouldBeFalse();
        byn.Note.ShouldBe("есть счёт «Карта BYN»");
        screen.Chosen.Select(c => c.Code).ShouldBe([CurrencyCode.Rub, CurrencyCode.Byn]);
    }

    [Fact]
    public async Task Screen_AnUnknownCode_CanBeAddedAsItIs()
    {
        var screen = await ScreenAsync();

        screen.Search = "xau";
        screen.HasCustomCode.ShouldBeTrue();
        screen.CustomTitle.ShouldBe("+ Добавить XAU");
        await screen.AddCustomCommand.ExecuteAsync(null);

        screen.Chosen.Last().Code.ShouldBe(CurrencyCode.Parse("XAU"));
        screen.HasCustomCode.ShouldBeFalse();
    }

    [Fact]
    public async Task Rates_WithOnlyTheBaseCurrency_ChooseNothingElse()
    {
        await Backend.SetCurrenciesAsync([], Ct);
        var rates = new ManualRatesViewModel(Backend, Time);
        await rates.LoadAsync(Ct);

        rates.Currencies.ShouldBe([CurrencyCode.Rub]);
        rates.From.ShouldBeNull();
        rates.Problem.ShouldBe("Выберите обе валюты.");
    }
}
