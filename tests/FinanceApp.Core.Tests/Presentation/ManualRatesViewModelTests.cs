using FinanceApp.Application.ExchangeRates;
using FinanceApp.Domain;
using FinanceApp.Presentation.Finance;

namespace FinanceApp.Core.Tests.Presentation;

public class ManualRatesViewModelTests : FinanceViewModelTestBase
{
    private async Task<ManualRatesViewModel> OpenAsync()
    {
        var viewModel = new ManualRatesViewModel(Backend, Time);
        await viewModel.LoadAsync(Ct);
        return viewModel;
    }

    [Fact]
    public async Task Opens_WithTheBaseCurrencyAsThePrice_AndToday()
    {
        var viewModel = await OpenAsync();

        viewModel.To.ShouldBe(CurrencyCode.Rub);
        viewModel.From.ShouldBe(CurrencyCode.Usd);
        viewModel.RateLabel.ShouldBe("1 USD =");
        viewModel.StartText.ShouldBe("15.10.2026");
        viewModel.Period.Period.ShouldBe(ManualRatePeriod.Week);
        viewModel.PeriodHint.ShouldBe("Действует 15–21 октября 2026");
        viewModel.CurrentHint.ShouldBe("Сейчас по ЦБ: 1 USD = 90 RUB");
        viewModel.HasRates.ShouldBeFalse();
    }

    [Fact]
    public async Task Add_KeepsTheRate_AndSaysTheTotalsChanged()
    {
        var viewModel = await OpenAsync();
        var changed = 0;
        viewModel.Changed += (_, _) => changed++;
        viewModel.ChooseFromCommand.Execute(CurrencyCode.Eur);
        viewModel.RateText = "101,5";
        viewModel.ChoosePeriodCommand.Execute(viewModel.Periods.Single(p => p.Period == ManualRatePeriod.Month));

        await viewModel.AddCommand.ExecuteAsync(null);

        Backend.ManualRates.ShouldHaveSingleItem().ShouldBe(
            new ManualRate(CurrencyCode.Eur, CurrencyCode.Rub, 101.5m, FakeFinanceBackend.Today, new DateOnly(2026, 11, 14)));
        viewModel.Rates.ShouldHaveSingleItem().Title.ShouldBe("1 EUR = 101,5 RUB");
        viewModel.RateText.ShouldBeEmpty();
        changed.ShouldBe(1);

        await viewModel.RemoveCommand.ExecuteAsync(viewModel.Rates[0]);
        Backend.ManualRates.ShouldBeEmpty();
        changed.ShouldBe(2);
    }

    [Fact]
    public async Task SameCurrencies_OrNoRate_CannotBeSaved()
    {
        var viewModel = await OpenAsync();

        viewModel.Problem.ShouldBe("Курс — положительное число.");
        viewModel.ChooseFromCommand.Execute(CurrencyCode.Rub);
        viewModel.RateText = "1";
        viewModel.Problem.ShouldBe("Валюты должны различаться.");
        viewModel.CanSave.ShouldBeFalse();
    }

    [Fact]
    public async Task Swap_TurnsTheRateAround()
    {
        var viewModel = await OpenAsync();
        viewModel.RateText = "4";

        viewModel.SwapCommand.Execute(null);

        viewModel.From.ShouldBe(CurrencyCode.Rub);
        viewModel.To.ShouldBe(CurrencyCode.Usd);
        viewModel.RateText.ShouldBe("0,25");
    }
}
