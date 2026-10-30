using FinanceApp.Domain;
using FinanceApp.Presentation.Setup;

namespace FinanceApp.Core.Tests.Presentation;

public class SetupViewModelTests
{
    private readonly FakeSetup _backend = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task FirstRun_GoesThroughTheTour_ThenCurrency_ThenBalances()
    {
        var setup = SetupViewModel.ForFirstRun(_backend);

        setup.IsTour.ShouldBeTrue();
        setup.Slide!.Title.ShouldBe("Добро пожаловать");
        setup.BackCommand.CanExecute(null).ShouldBeFalse();
        setup.Dots.Count.ShouldBe(SetupViewModel.Slides.Count + 2);

        for (var i = 0; i < SetupViewModel.Slides.Count; i++)
        {
            await setup.NextCommand.ExecuteAsync(null);
        }

        setup.IsCurrencyStep.ShouldBeTrue();
        setup.Slide.ShouldBeNull();
        await setup.NextCommand.ExecuteAsync(null);
        setup.IsBalancesStep.ShouldBeTrue();
        setup.NextText.ShouldBe("Начать");
        setup.Balances.Select(b => b.Account).ShouldBe(["Карта", "Наличные"]);
    }

    [Fact]
    public async Task RestoreFromACopy_ReplacesTheSetup()
    {
        var setup = SetupViewModel.ForFirstRun(_backend);
        var completed = false;
        setup.Completed += (_, _) => completed = true;
        setup.ShowsRestore.ShouldBeTrue();

        await setup.RestoreCommand.ExecuteAsync(null); // nothing chosen
        completed.ShouldBeFalse();

        _backend.RestoreAnswer = "В копии операций: 12, счетов: 2.";
        await setup.RestoreCommand.ExecuteAsync(null);
        completed.ShouldBeTrue();
        _backend.Choices.ShouldBeNull();
        SetupViewModel.ForTour().ShowsRestore.ShouldBeFalse();
    }

    [Fact]
    public void Skip_JumpsOverTheTour_ButNotOverTheSetup()
    {
        var setup = SetupViewModel.ForFirstRun(_backend);

        setup.SkipCommand.Execute(null);

        setup.IsCurrencyStep.ShouldBeTrue();
        _backend.Choices.ShouldBeNull();
    }

    [Fact]
    public async Task Finish_SavesTheCurrencyBalancesAndEnvelope()
    {
        var setup = SetupViewModel.ForFirstRun(_backend);
        var completed = false;
        setup.Completed += (_, _) => completed = true;
        setup.SkipCommand.Execute(null);
        setup.ChooseCurrencyCommand.Execute(SetupViewModel.Currencies.Single(c => c.Code == CurrencyCode.Usd));
        setup.CurrencySymbol.ShouldBe("$");
        await setup.NextCommand.ExecuteAsync(null);

        setup.Balances[0].AmountText = "1 250,50";
        setup.EnvelopeName = " Отпуск ";
        setup.EnvelopeGoalText = "3000";
        setup.QuickEntryLine.ShouldBeFalse(); // buttons by default; the line is a choice
        setup.QuickEntryLine = true;
        await setup.NextCommand.ExecuteAsync(null);

        completed.ShouldBeTrue();
        var choices = _backend.Choices.ShouldNotBeNull();
        choices.BaseCurrency.ShouldBe(CurrencyCode.Usd);
        choices.OpeningBalances.ShouldBe(new Dictionary<string, decimal> { ["Карта"] = 1250.50m });
        choices.Envelope.ShouldBe("Отпуск");
        choices.EnvelopeGoal.ShouldBe(3000m);
        choices.QuickEntryLine.ShouldBeTrue();
    }

    [Fact]
    public async Task WrongAmount_IsShown_AndNothingIsSaved()
    {
        var setup = SetupViewModel.ForFirstRun(_backend);
        setup.SkipCommand.Execute(null);
        await setup.NextCommand.ExecuteAsync(null);

        setup.Balances[1].AmountText = "много";
        await setup.NextCommand.ExecuteAsync(null);

        setup.Problem.ShouldContain("«Наличные»");
        _backend.Choices.ShouldBeNull();
    }

    [Fact]
    public async Task BackendFailure_IsShown_AndCanBeRetried()
    {
        _backend.Failure = new IOException("диск полон");
        var setup = SetupViewModel.ForFirstRun(_backend);
        setup.SkipCommand.Execute(null);
        await setup.NextCommand.ExecuteAsync(null);

        await setup.NextCommand.ExecuteAsync(null);

        setup.Problem.ShouldBe("Не получилось: диск полон");
        setup.NextCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public async Task TourOnly_ShowsJustTheSlides_AndSavesNothing()
    {
        var tour = SetupViewModel.ForTour();
        var completed = false;
        tour.Completed += (_, _) => completed = true;

        for (var i = 0; i < SetupViewModel.Slides.Count - 1; i++)
        {
            await tour.NextCommand.ExecuteAsync(null);
        }

        tour.NextText.ShouldBe("Понятно");
        await tour.NextCommand.ExecuteAsync(null);
        completed.ShouldBeTrue();
        tour.IsCurrencyStep.ShouldBeFalse();
    }

    private sealed class FakeSetup : ISetupBackend
    {
        public SetupChoices? Choices { get; private set; }

        public Exception? Failure { get; set; }

        public Task CompleteAsync(SetupChoices choices, CancellationToken cancellationToken = default)
        {
            if (Failure is not null)
            {
                return Task.FromException(Failure);
            }

            Choices = choices;
            return Task.CompletedTask;
        }

        public string? RestoreAnswer { get; set; }

        public Task<string?> RestoreFromCopyAsync(CancellationToken cancellationToken = default) =>
            Failure is not null ? Task.FromException<string?>(Failure) : Task.FromResult(RestoreAnswer);
    }
}
