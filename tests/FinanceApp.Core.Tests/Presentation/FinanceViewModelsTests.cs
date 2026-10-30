using FinanceApp.Domain;
using FinanceApp.Presentation.Finance;
using Microsoft.Extensions.Time.Testing;

namespace FinanceApp.Core.Tests.Presentation;

public abstract class FinanceViewModelTestBase
{
    protected FinanceViewModelTestBase()
    {
        Car = Backend.Add(new Account(Guid.NewGuid(), "Машина", CurrencyCode.Rub, 5, AccountKind.Envelope));
    }

    protected FakeFinanceBackend Backend { get; } = new();

    protected FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));

    protected Account Car { get; }

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;
}

public class OperationsViewModelTests : FinanceViewModelTestBase
{
    private async Task<OperationsViewModel> ShowAsync(Account? account = null)
    {
        var viewModel = new OperationsViewModel(Backend, Time);
        await viewModel.ShowAsync(account, Ct);
        return viewModel;
    }

    [Fact]
    public async Task AllOperations_GroupedByDay_NewestFirst_WithDayTotals()
    {
        Backend.Spend("2026-10-15", 350m, "Еда", note: "кофе");
        Backend.Spend("2026-10-15", 1_200m, "Транспорт");
        Backend.Earn("2026-10-12", 50_000m);
        Backend.Move("2026-10-14", 10_000m, Backend.Card, Car);

        var viewModel = await ShowAsync();

        viewModel.Title.ShouldBe("Все операции");
        viewModel.Days.Select(d => (d.Title, d.TotalText)).ShouldBe(
        [
            ("Сегодня", "−1 550 ₽"),
            ("Вчера", "0 ₽"),
            ("12 октября, пн", "+50 000 ₽"),
        ]);
        viewModel.Days[0].Rows[0].Title.ShouldBe("Транспорт");
        viewModel.Days[0].Rows[1].Subtitle.ShouldBe("кофе · Карта");
        viewModel.Days[1].Rows[0].Title.ShouldBe("Карта → Машина");
        viewModel.Subtitle.ShouldBe("За месяц: +50 000 ₽ / −1 550 ₽");
    }

    [Fact]
    public async Task AccountPage_WordsTransfersFromItsPointOfView()
    {
        Backend.Move("2026-10-14", 10_000m, Backend.Card, Car);
        Backend.Spend("2026-10-15", 300m, "Еда", Backend.Cash);

        var card = await ShowAsync(Backend.Card);
        var envelope = await ShowAsync(Car);

        card.Title.ShouldBe("Карта");
        var outgoing = card.Days.ShouldHaveSingleItem().Rows.ShouldHaveSingleItem();
        outgoing.Title.ShouldBe("В конверт «Машина»");
        outgoing.AmountText.ShouldBe("−10 000 ₽");
        outgoing.Tone.ShouldBe(AmountTone.Envelope);

        var incoming = envelope.Days.ShouldHaveSingleItem().Rows.ShouldHaveSingleItem();
        incoming.Title.ShouldBe("Перевод ← «Карта»");
        incoming.AmountText.ShouldBe("+10 000 ₽");
        envelope.Subtitle.ShouldBe("За месяц: +10 000 ₽ / 0 ₽");
    }

    [Fact]
    public async Task Filters_ByTypeAndSearch()
    {
        Backend.Spend("2026-10-15", 350m, "Еда", note: "кофе");
        Backend.Spend("2026-10-14", 1_200m, "Транспорт", note: "такси");
        Backend.Move("2026-10-14", 10_000m, Backend.Card, Car);
        var viewModel = await ShowAsync();

        viewModel.TypeFilter = OperationsViewModel.TypeChoices.Single(c => c.Type == TransactionType.Transfer);
        viewModel.Days.SelectMany(d => d.Rows).ShouldHaveSingleItem().Transaction.IsTransfer.ShouldBeTrue();

        viewModel.TypeFilter = OperationsViewModel.TypeChoices[0];
        viewModel.Search = "такс";
        viewModel.Days.SelectMany(d => d.Rows).ShouldHaveSingleItem().Title.ShouldBe("Транспорт");

        viewModel.Search = "ничего такого";
        viewModel.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public async Task MonthNavigation()
    {
        Backend.Spend("2026-09-20", 100m, "Еда");
        var viewModel = await ShowAsync();
        viewModel.IsEmpty.ShouldBeTrue();

        await viewModel.PreviousMonthCommand.ExecuteAsync(null);

        viewModel.MonthTitle.ShouldBe("Сентябрь 2026");
        viewModel.IsEmpty.ShouldBeFalse();
    }

    [Fact]
    public async Task Delete_ThenUndo_RestoresAndReportsChanges()
    {
        var coffee = Backend.Spend("2026-10-15", 350m, "Еда");
        var viewModel = await ShowAsync();
        var changes = 0;
        viewModel.Changed += (_, _) => changes++;

        await viewModel.DeleteCommand.ExecuteAsync(viewModel.Days[0].Rows[0]);
        viewModel.IsEmpty.ShouldBeTrue();
        viewModel.UndoText.ShouldBe("Удалено: Еда −350 ₽");

        await viewModel.UndoCommand.ExecuteAsync(null);

        viewModel.Days[0].Rows.ShouldHaveSingleItem().Id.ShouldBe(coffee.Id);
        viewModel.CanUndo.ShouldBeFalse();
        changes.ShouldBe(2);
    }

    [Fact]
    public async Task Edit_RaisesEditRequested()
    {
        var coffee = Backend.Spend("2026-10-15", 350m, "Еда");
        var viewModel = await ShowAsync();
        Transaction? requested = null;
        viewModel.EditRequested += (_, t) => requested = t;

        viewModel.EditCommand.Execute(viewModel.Days[0].Rows[0]);

        requested.ShouldBe(coffee);
    }
}

public class TransactionFormViewModelTests : FinanceViewModelTestBase
{
    private async Task<TransactionFormViewModel> NewAsync(TransactionType kind, Account? account = null, Account? toAccount = null) =>
        TransactionFormViewModel.ForNew(Backend, await Backend.LoadCatalogAsync(Ct), FakeFinanceBackend.Today, kind, account, toAccount);

    [Fact]
    public async Task NewExpense_DefaultsToToday_AndOrdersTilesByUsage()
    {
        Backend.Spend("2026-10-01", 1m, "Транспорт");
        Backend.Spend("2026-10-02", 1m, "Транспорт");
        Backend.Spend("2026-10-03", 1m, "Еда", Backend.Cash);

        var form = await NewAsync(TransactionType.Expense);

        form.Title.ShouldBe("Новый расход");
        form.DateText.ShouldBe("15.10.2026");
        form.Account.ShouldBe(Backend.Cash); // last regular account used for expenses
        form.Tiles.Select(t => t.Name).Take(2).ShouldBe(["Транспорт", "Еда"]);
        form.CanSave.ShouldBeFalse();
        form.Problem!.ShouldContain("сумму");
    }

    [Fact]
    public async Task Expense_SavesThroughBackend()
    {
        var form = await NewAsync(TransactionType.Expense, Backend.Card);
        var saved = false;
        form.Saved += (_, _) => saved = true;

        form.AmountText = "1 200,50";
        form.ChooseCategoryCommand.Execute(form.Tiles.Single(t => t.Name == "Транспорт"));
        form.YesterdayCommand.Execute(null);
        form.Note = "такси";
        await form.SaveCommand.ExecuteAsync(null);

        saved.ShouldBeTrue();
        var t = Backend.Transactions.ShouldHaveSingleItem();
        t.Amount.ShouldBe(1200.50m);
        t.Date.ShouldBe(new DateOnly(2026, 10, 14));
        t.Note.ShouldBe("такси");
        form.Tiles.Single(x => x.Name == "Транспорт").IsSelected.ShouldBeTrue();
    }

    [Fact]
    public async Task SwitchingToIncome_OffersIncomeCategories()
    {
        var form = await NewAsync(TransactionType.Expense);

        form.ChooseKindCommand.Execute(TransactionType.Income);

        form.Title.ShouldBe("Новый доход");
        form.Tiles.ShouldAllBe(t => t.Category.Type == TransactionType.Income);
        form.Category.ShouldBeNull();
    }

    [Fact]
    public async Task FillEnvelope_TransferFromDefaultAccount()
    {
        var form = await NewAsync(TransactionType.Transfer, toAccount: Car);

        form.Account.ShouldBe(Backend.Card);
        form.ToAccount.ShouldBe(Car);
        form.Tiles.ShouldBeEmpty();
        form.NeedsReceivedAmount.ShouldBeFalse();

        form.AmountText = "10000";
        await form.SaveCommand.ExecuteAsync(null);

        var t = Backend.Transactions.ShouldHaveSingleItem();
        t.IsTransfer.ShouldBeTrue();
        t.ToAccountId.ShouldBe(Car.Id);
    }

    [Fact]
    public async Task Presets_FromAnotherLoad_AreMatchedById_SoDropdownsShowThem()
    {
        // The sidebar's balances and the form's catalog are separate loads: equal ids, different instances.
        var carCopy = new Account(Car.Id, Car.Name, Car.Currency, kind: AccountKind.Envelope);

        var form = await NewAsync(TransactionType.Transfer, toAccount: carCopy);

        form.ToAccount.ShouldBeSameAs(Car);
        form.Accounts.ShouldContain(form.ToAccount!);
        form.Accounts.ShouldContain(form.Account!);
    }

    [Fact]
    public async Task Transfer_ToTheSameAccount_IsAProblem()
    {
        var form = await NewAsync(TransactionType.Transfer, Backend.Card, Backend.Cash);
        form.AmountText = "100";
        form.ToAccount = Backend.Card;

        form.Problem!.ShouldContain("различаться");
    }

    [Fact]
    public async Task Transfer_BetweenCurrencies_AsksForReceivedAmount()
    {
        var usd = Backend.Add(new Account(Guid.NewGuid(), "Наличные USD", CurrencyCode.Usd));
        var form = await NewAsync(TransactionType.Transfer, Backend.Card, usd);

        form.NeedsReceivedAmount.ShouldBeTrue();
        form.ReceivedCurrency.ShouldBe("USD");
        form.AmountText = "9000";
        form.ReceivedText = "100";
        await form.SaveCommand.ExecuteAsync(null);

        Backend.Transactions.ShouldHaveSingleItem().ToAmount.ShouldBe(100m);
    }

    [Fact]
    public async Task Edit_PrefillsAndUpdates()
    {
        var coffee = Backend.Spend("2026-10-10", 350m, "Еда", note: "кофе");
        var form = TransactionFormViewModel.ForEdit(Backend, await Backend.LoadCatalogAsync(Ct), FakeFinanceBackend.Today, coffee);

        form.Title.ShouldBe("Изменить расход");
        form.AmountText.ShouldBe("350");
        form.DateText.ShouldBe("10.10.2026");
        form.Category!.Name.ShouldBe("Еда");
        form.Tiles.Single(t => t.Name == "Еда").IsSelected.ShouldBeTrue();

        form.AmountText = "400";
        await form.SaveCommand.ExecuteAsync(null);

        Backend.Transactions.ShouldHaveSingleItem().Amount.ShouldBe(400m);
    }

    [Fact]
    public async Task SaveError_IsShown_AndFormStaysOpen()
    {
        var form = await NewAsync(TransactionType.Expense, Backend.Card);
        form.AmountText = "100";
        form.ChooseCategoryCommand.Execute(form.Tiles[0]);
        Backend.Failure = new DomainException("Нет курса USD");
        var saved = false;
        form.Saved += (_, _) => saved = true;

        await form.SaveCommand.ExecuteAsync(null);

        saved.ShouldBeFalse();
        form.SaveError.ShouldBe("Нет курса USD");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("10,555")]
    [InlineData("сто")]
    public async Task InvalidAmount_BlocksSave(string amount)
    {
        var form = await NewAsync(TransactionType.Expense, Backend.Card);
        form.ChooseCategoryCommand.Execute(form.Tiles[0]);

        form.AmountText = amount;

        form.CanSave.ShouldBeFalse();
    }
}

/// <summary>A receipt in another currency than the account: converted at the CBR rate or at what the bank charged.</summary>
public class ReceiptCurrencyTests : FinanceViewModelTestBase
{
    private async Task<TransactionFormViewModel> NewExpenseAsync() =>
        TransactionFormViewModel.ForNew(Backend, await Backend.LoadCatalogAsync(Ct), FakeFinanceBackend.Today, TransactionType.Expense, Backend.Card);

    [Fact]
    public async Task ByDefault_TheReceiptIsInTheAccountCurrency()
    {
        var form = await NewExpenseAsync();

        form.PaidCurrency.ShouldBe(CurrencyCode.Rub);
        form.PaidCurrencies.Take(3).ShouldBe([CurrencyCode.Rub, CurrencyCode.Usd, CurrencyCode.Eur]);
        form.IsForeign.ShouldBeFalse();
        form.ConversionText.ShouldBeEmpty();
    }

    [Fact]
    public async Task Euros_AreConverted_AtTheCbrRate()
    {
        var form = await NewExpenseAsync();
        form.AmountText = "20";
        form.ChooseCategoryCommand.Execute(form.Tiles.First(t => t.Name == "Еда"));

        form.ChoosePaidCurrencyCommand.Execute(CurrencyCode.Eur);

        form.IsForeign.ShouldBeTrue();
        form.AmountCurrency.ShouldBe("EUR");
        form.ConversionText.ShouldBe("≈ 2\u00a0000 ₽ по курсу ЦБ на 15.10.2026");
        await form.SaveCommand.ExecuteAsync(null);
        form.SaveError.ShouldBeEmpty();
    }

    [Fact]
    public async Task WithoutARate_WhatWasChargedIsAsked()
    {
        Backend.RubRates.Remove(CurrencyCode.Eur);
        var form = await NewExpenseAsync();
        form.AmountText = "20";
        form.ChooseCategoryCommand.Execute(form.Tiles.First(t => t.Name == "Еда"));

        form.ChoosePaidCurrencyCommand.Execute(CurrencyCode.Eur);

        form.RateMissing.ShouldBeTrue();
        form.Problem.ShouldBe("Нет курса EUR — укажите, сколько списано со счёта.");
        form.ChargedText = "2150";
        form.Problem.ShouldBeNull();
    }

    [Fact]
    public async Task Transfers_HaveNoReceiptCurrency()
    {
        var form = await NewExpenseAsync();
        form.ChoosePaidCurrencyCommand.Execute(CurrencyCode.Eur);

        form.ChooseKindCommand.Execute(TransactionType.Transfer);

        form.IsForeign.ShouldBeFalse();
        form.AmountCurrency.ShouldBe("RUB");
    }
}

public class AccountFormViewModelTests : FinanceViewModelTestBase
{
    [Fact]
    public async Task NewEnvelope_WithOptionalGoal()
    {
        var form = AccountFormViewModel.ForNew(Backend, AccountKind.Envelope, CurrencyCode.Rub);
        form.Title.ShouldBe("Новый конверт");
        form.HasGoal.ShouldBeFalse();

        form.Name = "Отдых";
        form.AddGoalCommand.Execute(null);
        form.GoalAmountText = "150 000";
        form.GoalDateText = "01.05.2027";
        await form.SaveCommand.ExecuteAsync(null);

        var vacation = Backend.Accounts.Single(a => a.Name == "Отдых");
        vacation.IsEnvelope.ShouldBeTrue();
        vacation.GoalAmount.ShouldBe(150_000m);
        vacation.GoalDate.ShouldBe(new DateOnly(2027, 5, 1));
    }

    [Fact]
    public void Validation()
    {
        var form = AccountFormViewModel.ForNew(Backend, AccountKind.Regular, CurrencyCode.Rub);
        form.Problem.ShouldBe("Введите название.");

        form.Name = "Кошелёк";
        form.CurrencyText = "рубли";
        form.Problem!.ShouldContain("Валюта");

        form.CurrencyText = "usd";
        form.OpeningBalanceText = "много";
        form.Problem!.ShouldContain("остаток");

        form.OpeningBalanceText = "-1 500";
        form.CanSave.ShouldBeTrue();
    }

    [Fact]
    public async Task Edit_RemovesGoal_AndHides()
    {
        Car.SetGoal(100_000m, null);
        var form = AccountFormViewModel.ForEdit(Backend, Car);
        form.HasGoal.ShouldBeTrue();

        form.RemoveGoalCommand.Execute(null);
        await form.SaveCommand.ExecuteAsync(null);
        await form.ToggleHiddenCommand.ExecuteAsync(null);

        Car.GoalAmount.ShouldBeNull();
        Car.IsHidden.ShouldBeTrue();
    }
}

public class SummaryViewModelTests : FinanceViewModelTestBase
{
    private async Task<SummaryViewModel> OpenAsync()
    {
        var viewModel = new SummaryViewModel(Backend, Time);
        await viewModel.LoadAsync(Ct);
        return viewModel;
    }

    [Fact]
    public async Task Month_WhereIncomeWent()
    {
        Backend.Earn("2026-10-01", 50_000m);
        Backend.Spend("2026-10-05", 1_500m, "Еда");
        Backend.Spend("2026-10-06", 500m, "Транспорт");
        Backend.Move("2026-10-06", 10_000m, Backend.Card, Car);

        var viewModel = await OpenAsync();

        viewModel.Title.ShouldBe("Октябрь 2026");
        viewModel.ProgressText.ShouldBe("15 из 31 дня");
        viewModel.HasFlow.ShouldBeTrue();
        viewModel.IncomeText.ShouldBe("Доход 50 000 ₽");
        viewModel.FlowSegments.Select(s => (s.Label, s.Tone)).ShouldBe(
        [
            ("Еда", FlowTone.First),
            ("Транспорт", FlowTone.Second),
            ("в конверты", FlowTone.Envelopes),
            ("осталось", FlowTone.Free),
        ]);
        viewModel.IncomeShare.ShouldBe(1d);
        viewModel.OutflowShare.ShouldBe(1d);
        viewModel.FlowNote.ShouldBe("Свободными осталось 38 000 ₽");
        viewModel.FlowNoteIsWarning.ShouldBeFalse();
    }

    [Fact]
    public async Task Month_Cards_ComparedWithTheSameDayOfLastMonth()
    {
        Backend.Earn("2026-10-01", 50_000m);
        Backend.Spend("2026-10-05", 1_500m, "Еда");
        Backend.Spend("2026-10-06", 500m, "Транспорт");
        Backend.Move("2026-10-06", 10_000m, Backend.Card, Car);
        Backend.Spend("2026-09-05", 1_000m, "Еда");
        Backend.Spend("2026-09-20", 3_000m, "Еда");

        var viewModel = await OpenAsync();

        viewModel.Cards.Select(c => c.Title).ShouldBe(["Отложено", "Расходы", "В среднем в день", "К концу месяца"]);
        viewModel.Cards[0].ValueText.ShouldStartWith("20");
        viewModel.Cards[1].Note.ShouldBe("+100 % к 15 сентября");
        viewModel.Cards[1].Tone.ShouldBe(NoteTone.Bad);
        viewModel.Cards[3].ValueText.ShouldBe("≈ 5 000 ₽"); // 2 000 so far + 3 000 that September spent later
        viewModel.Cards[3].Note.ShouldBe("если дальше как в прошлом месяце");
        viewModel.Categories.Select(c => (c.Name, c.ChangeText)).ShouldBe([("Еда", "−63 %"), ("Транспорт", "новое")]);
        viewModel.Categories[0].ChangeTone.ShouldBe(NoteTone.Good);
    }

    [Fact]
    public async Task WithoutHistory_NothingIsComparedWithZeros()
    {
        Backend.Spend("2026-10-05", 1_500m, "Еда");

        var viewModel = await OpenAsync();

        viewModel.Cards[1].Note.ShouldBeEmpty();
        viewModel.Cards[2].Note.ShouldBeEmpty();
        viewModel.Cards[3].Note.ShouldBe("при таком темпе");
        viewModel.Categories.ShouldHaveSingleItem().ChangeText.ShouldBeEmpty();
        viewModel.HasPreviousPace.ShouldBeFalse();
        viewModel.PaceCaption.ShouldBe("1\u00a0500 ₽ к 15 октября");
    }

    [Fact]
    public async Task SpendingMoreThanIncome_IsAWarning()
    {
        Backend.Spend("2026-10-03", 5_000m, "Еда");

        var viewModel = await OpenAsync();

        viewModel.FlowNoteIsWarning.ShouldBeTrue();
        viewModel.FlowNote.ShouldBe("Дохода не было — 5 000 ₽ взято из остатков на счетах");
    }

    [Fact]
    public async Task Pace_ByDay_UpToToday()
    {
        Backend.Spend("2026-09-05", 1_000m, "Еда");
        Backend.Spend("2026-10-05", 2_000m, "Еда");

        var viewModel = await OpenAsync();

        viewModel.PaceTitle.ShouldBe("Темп трат по дням");
        viewModel.Pace.Slots.ShouldBe(31);
        viewModel.Pace.Current.Count.ShouldBe(15);
        viewModel.Pace.Current[^1].ShouldBe(1d);
        viewModel.Pace.Previous[^1].ShouldBe(0.5);
        viewModel.HasPreviousPace.ShouldBeTrue();
        viewModel.PreviousLegend.ShouldBe("сентябрь");
        viewModel.PaceCaption.ShouldBe("2 000 ₽ к 15 октября · в прошлом месяце к этому дню 1 000 ₽");
        (viewModel.PaceStartLabel, viewModel.PaceEndLabel).ShouldBe(("1 окт", "31 окт"));
    }

    [Fact]
    public async Task TopExpenses_AndIncomes()
    {
        Backend.Earn("2026-10-01", 60_000m);
        Backend.Spend("2026-10-03", 35_000m, "Жильё", note: "аренда");
        Backend.Spend("2026-10-05", 500m, "Еда");
        Backend.Spend("2026-10-07", 4_000m, "Развлечения", Car);

        var viewModel = await OpenAsync();

        viewModel.TopExpenses.Select(r => (r.Title, r.Subtitle)).ShouldBe(
        [
            ("Аренда", "3 окт · Жильё"),
            ("Развлечения", "7 окт · из «Машина»"),
            ("Еда", "5 окт"),
        ]);
        viewModel.Incomes.ShouldHaveSingleItem().Title.ShouldBe("Зарплата");
        viewModel.FlowNote.ShouldContain("ещё 4 000 ₽ потрачено из конвертов");
    }

    [Fact]
    public async Task Envelopes_GoalOnTrackOrNot()
    {
        Car.SetGoal(150_000m, new DateOnly(2027, 4, 30));
        Backend.Move("2026-10-02", 10_000m, Backend.Card, Car);

        var viewModel = await OpenAsync();

        var car = viewModel.Envelopes.ShouldHaveSingleItem();
        car.Tone.ShouldBe(NoteTone.Bad);
        car.Note.ShouldBe("+10 000 ₽ · 10 000 ₽ из 150 000 ₽");
        viewModel.EnvelopeWarnings.ShouldHaveSingleItem().ShouldBe(
            "Машина: при 10 000 ₽ в месяц цель наберётся через 14 мес. — чтобы успеть к сроку (апрель 2027), нужно 20 000 ₽ в месяц");

        Backend.Move("2026-10-03", 10_000m, Backend.Card, Car);
        await viewModel.LoadAsync(Ct);

        viewModel.Envelopes[0].Tone.ShouldBe(NoteTone.Good);
        viewModel.Envelopes[0].Note.ShouldEndWith("в срок");
        viewModel.EnvelopeWarnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task Year_ByMonth_AndNavigates()
    {
        Backend.Spend("2026-02-10", 100m, "Еда");
        Backend.Spend("2026-10-10", 300m, "Еда");
        var viewModel = await OpenAsync();

        viewModel.Choice = SummaryViewModel.Choices.Single(c => c.Kind == PeriodKind.Year);

        viewModel.Title.ShouldBe("2026 год");
        viewModel.PaceTitle.ShouldBe("Темп трат по месяцам");
        viewModel.Pace.Slots.ShouldBe(12);
        viewModel.Pace.Bars[9].ShouldBe(1d);
        viewModel.Cards[3].Title.ShouldBe("К концу года");
        viewModel.NextCommand.CanExecute(null).ShouldBeFalse(); // 2027 has not started

        await viewModel.PreviousCommand.ExecuteAsync(null);
        viewModel.Title.ShouldBe("2025 год");
        viewModel.Cards[3].Title.ShouldBe("Свободный остаток");
    }

    [Fact]
    public async Task AllTime_HasNoComparison()
    {
        Backend.Spend("2026-10-10", 300m, "Еда");
        var viewModel = await OpenAsync();

        viewModel.Choice = SummaryViewModel.Choices.Single(c => c.Kind == PeriodKind.AllTime);

        viewModel.Title.ShouldBe("Всё время");
        viewModel.Cards[1].Note.ShouldBeEmpty();
        viewModel.Cards[3].Title.ShouldBe("Свободный остаток");
        viewModel.HasPreviousPace.ShouldBeFalse();
        viewModel.PreviousCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public async Task CustomRange()
    {
        Backend.Spend("2026-10-03", 300m, "Еда");
        var viewModel = await OpenAsync();

        viewModel.Choice = SummaryViewModel.Choices.Single(c => c.Kind == PeriodKind.Custom);
        viewModel.CustomFrom = "01.10.2026";
        viewModel.CustomTo = "05.10.2026";
        await viewModel.ApplyCustomCommand.ExecuteAsync(null);

        viewModel.IsCustom.ShouldBeTrue();
        viewModel.Title.ShouldBe("1–5 октября 2026");
        viewModel.Cards[1].ValueText.ShouldBe("300 ₽");
    }

    [Fact]
    public async Task Week_Title()
    {
        var viewModel = await OpenAsync();

        viewModel.Choice = SummaryViewModel.Choices.Single(c => c.Kind == PeriodKind.Week);

        viewModel.Title.ShouldBe("12–18 октября 2026");
        viewModel.Cards[3].Title.ShouldBe("К концу недели");
    }
}

public class ShellViewModelTests : FinanceViewModelTestBase
{
    private async Task<ShellViewModel> OpenAsync()
    {
        var shell = new ShellViewModel(Backend, Time);
        await shell.RefreshAsync(Ct);
        return shell;
    }

    [Fact]
    public async Task Sidebar_ShowsBalancesTotalsAndGoals()
    {
        Car.SetGoal(150_000m, new DateOnly(2027, 4, 30));
        Backend.Earn("2026-10-01", 100_000m);
        Backend.Move("2026-10-02", 60_000m, Backend.Card, Car);

        var shell = await OpenAsync();

        shell.TotalText.ShouldBe("100 000 ₽");
        shell.AccountsTotalText.ShouldBe("40 000 ₽");
        shell.EnvelopesTotalText.ShouldBe("60 000 ₽");
        shell.Accounts.Select(a => (a.Title, a.BalanceText)).ShouldBe([("Карта", "40 000 ₽"), ("Наличные", "0 ₽")]);
        var car = shell.Envelopes.ShouldHaveSingleItem();
        car.Progress.ShouldBe(0.4);
        car.Hint!.ShouldContain("по 12 857 ₽ в месяц");
    }

    [Fact]
    public async Task SelectingAnAccount_ShowsItsOperations_AndSummarySwitches()
    {
        Backend.Spend("2026-10-15", 300m, "Еда", Backend.Cash);
        Backend.Spend("2026-10-15", 500m, "Еда");
        var shell = await OpenAsync();

        shell.Selected = shell.Accounts.Single(a => a.Title == "Наличные");
        shell.Operations.Title.ShouldBe("Наличные");
        shell.Operations.Days.SelectMany(d => d.Rows).ShouldHaveSingleItem();

        shell.Selected = shell.SummaryItem;
        shell.IsSummary.ShouldBeTrue();
        shell.Summary.Cards.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task NewExpense_FromAccountPage_PresetsAccount_AndRefreshesAfterSave()
    {
        var shell = await OpenAsync();
        shell.Selected = shell.Accounts.Single(a => a.Title == "Наличные");

        await shell.NewExpenseCommand.ExecuteAsync(null);
        var form = shell.Form.ShouldNotBeNull();
        form.Account.ShouldBe(Backend.Cash);

        form.AmountText = "250";
        form.ChooseCategoryCommand.Execute(form.Tiles[0]);
        await form.SaveCommand.ExecuteAsync(null);

        shell.Form.ShouldBeNull();
        shell.Accounts.Single(a => a.Title == "Наличные").BalanceText.ShouldBe("−250 ₽");
        shell.Operations.Days.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task EnvelopePage_FillAndTake()
    {
        var shell = await OpenAsync();
        shell.Selected = shell.Envelopes.Single();
        shell.IsEnvelopeSelected.ShouldBeTrue();

        await shell.FillEnvelopeCommand.ExecuteAsync(null);
        shell.Form!.ToAccount.ShouldBe(Car);
        shell.Form.Account.ShouldBe(Backend.Card);

        await shell.TakeFromEnvelopeCommand.ExecuteAsync(null);
        shell.Form!.Account.ShouldBe(Car);
        shell.Form.ToAccount.ShouldBe(Backend.Card);
    }

    [Fact]
    public async Task NewEnvelope_IsSelectedAfterSaving()
    {
        var shell = await OpenAsync();

        await shell.NewEnvelopeCommand.ExecuteAsync(null);
        var form = shell.AccountForm.ShouldNotBeNull();
        form.Name = "Велосипед";
        await form.SaveCommand.ExecuteAsync(null);

        shell.AccountForm.ShouldBeNull();
        shell.Selected.Title.ShouldBe("Велосипед");
        shell.Envelopes.Select(e => e.Title).ShouldBe(["Машина", "Велосипед"]);
    }

    [Fact]
    public async Task HiddenAccounts_AppearOnlyOnRequest()
    {
        Backend.Cash.Hide();
        var shell = await OpenAsync();
        shell.Accounts.ShouldNotContain(a => a.Title == "Наличные");

        shell.ShowHidden = true;

        shell.Accounts.ShouldContain(a => a.Title == "Наличные");
    }
}
