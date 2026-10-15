using FinanceApp.Application.Accounts;
using FinanceApp.Application.Abstractions;
using FinanceApp.Application.QuickEntry;
using FinanceApp.Application.Transactions;
using FinanceApp.Domain;

namespace FinanceApp.Infrastructure.Tests.Accounts;

public class AccountsAndTransfersTests
{
    private static readonly DateOnly Oct15 = new(2026, 10, 15);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<Account> CreateAsync(TestApp app, AccountInput input)
    {
        await using var scope = app.Scope();
        return await scope.Get<AccountService>().CreateAsync(input, Ct);
    }

    private static async Task<Account> FindAsync(TestApp app, string name)
    {
        await using var scope = app.Scope();
        return (await scope.Get<IAccountRepository>().ListAsync(Ct)).Single(a => a.Name == name);
    }

    private static async Task<SavedTransaction> TransferAsync(TestApp app, TransferInput input)
    {
        await using var scope = app.Scope();
        return await scope.Get<TransactionService>().AddTransferAsync(input, Ct);
    }

    private static async Task<BalancesOverview> BalancesAsync(TestApp app)
    {
        await using var scope = app.Scope();
        return await scope.Get<AccountBalancesService>().LoadAsync(cancellationToken: Ct);
    }

    [Fact]
    public async Task CreateEnvelope_WithGoal_InBaseCurrency()
    {
        await using var app = await TestApp.CreateAsync(exportEnabled: false);

        var car = await CreateAsync(app, new AccountInput("Машина", AccountKind.Envelope, GoalAmount: 150_000m, GoalDate: new DateOnly(2027, 5, 1)));

        car.IsEnvelope.ShouldBeTrue();
        car.Currency.ShouldBe(CurrencyCode.Rub);
        car.GoalAmount.ShouldBe(150_000m);
        (await FindAsync(app, "Машина")).GoalDate.ShouldBe(new DateOnly(2027, 5, 1));
    }

    [Fact]
    public async Task DuplicateName_IsRejected_CaseInsensitively()
    {
        await using var app = await TestApp.CreateAsync(exportEnabled: false);

        await Should.ThrowAsync<DomainException>(() => CreateAsync(app, new AccountInput("карта")));
    }

    [Fact]
    public async Task Update_RenamesAndSetsOpeningBalance_ButKeepsKind()
    {
        await using var app = await TestApp.CreateAsync(exportEnabled: false);
        var cash = await FindAsync(app, "Наличные");

        await using (var scope = app.Scope())
        {
            await scope.Get<AccountService>().UpdateAsync(cash.Id, new AccountInput("Кошелёк", OpeningBalance: 2_500m), Ct);
            await Should.ThrowAsync<DomainException>(() =>
                scope.Get<AccountService>().UpdateAsync(cash.Id, new AccountInput("Кошелёк", AccountKind.Envelope), Ct));
        }

        var renamed = await FindAsync(app, "Кошелёк");
        renamed.OpeningBalance.ShouldBe(2_500m);
        renamed.Kind.ShouldBe(AccountKind.Regular);
    }

    [Fact]
    public async Task Transfer_SameCurrency_MovesBalances()
    {
        await using var app = await TestApp.CreateAsync(exportEnabled: false);
        var card = await FindAsync(app, "Карта");
        var car = await CreateAsync(app, new AccountInput("Машина", AccountKind.Envelope));

        var saved = await TransferAsync(app, new TransferInput(Oct15, 10_000m, card.Id, car.Id, "в конверт"));

        saved.Transaction.ToAmount.ShouldBe(10_000m);
        var balances = await BalancesAsync(app);
        balances.Accounts.Single(b => b.Account.Name == "Карта").Balance.ShouldBe(-10_000m);
        balances.Envelopes.ShouldHaveSingleItem().Balance.ShouldBe(10_000m);
        balances.EnvelopesTotal.ShouldBe(10_000m);
        balances.Total.ShouldBe(0m);
    }

    [Fact]
    public async Task Transfer_ToForeignAccount_UsesCrossRateByDefault_OrTheEnteredAmount()
    {
        await using var app = await TestApp.CreateAsync(exportEnabled: false);
        app.Rates.Usd = 90m;
        var card = await FindAsync(app, "Карта");
        var usd = await CreateAsync(app, new AccountInput("Наличные USD", Currency: CurrencyCode.Usd));

        var byRate = await TransferAsync(app, new TransferInput(Oct15, 9_000m, card.Id, usd.Id, null));
        var entered = await TransferAsync(app, new TransferInput(Oct15, 9_300m, card.Id, usd.Id, "обменник", ToAmount: 100m));

        byRate.Transaction.ToAmount.ShouldBe(100m);
        entered.Transaction.ToAmount.ShouldBe(100m);
        var balances = await BalancesAsync(app);
        var usdBalance = balances.Accounts.Single(b => b.Account.Name == "Наличные USD");
        usdBalance.Balance.ShouldBe(200m);
        usdBalance.BaseBalance.ShouldBe(18_000m);
    }

    [Fact]
    public async Task Balance_IsOpeningBalancePlusTransactions()
    {
        await using var app = await TestApp.CreateAsync(exportEnabled: false);
        var cash = await FindAsync(app, "Наличные");
        await using (var scope = app.Scope())
        {
            await scope.Get<AccountService>().UpdateAsync(cash.Id, new AccountInput("Наличные", OpeningBalance: 5_000m), Ct);
            await scope.Get<QuickEntryService>().SaveAsync("350 еда кофе @нал", Ct);
            await scope.Get<QuickEntryService>().SaveAsync("+1000 прочее @нал", Ct);
        }

        var balances = await BalancesAsync(app);

        balances.Accounts.Single(b => b.Account.Name == "Наличные").Balance.ShouldBe(5_650m);
        balances.FreeTotal.ShouldBe(5_650m);
    }

    [Fact]
    public async Task EnvelopeGoal_ProgressAndMonthlyContribution()
    {
        await using var app = await TestApp.CreateAsync(exportEnabled: false);
        var card = await FindAsync(app, "Карта");
        var car = await CreateAsync(app, new AccountInput("Машина", AccountKind.Envelope, GoalAmount: 150_000m, GoalDate: new DateOnly(2027, 4, 30)));
        await TransferAsync(app, new TransferInput(Oct15, 60_000m, card.Id, car.Id, null));

        var envelope = (await BalancesAsync(app)).Envelopes.ShouldHaveSingleItem();

        envelope.GoalProgress.ShouldBe(0.4m);
        envelope.MonthlyToGoal(Oct15).ShouldBe(12_857m); // 90 000 over Oct..Apr = 7 months
    }

    [Fact]
    public async Task HiddenAccounts_AreLeftOut_UnlessAsked()
    {
        await using var app = await TestApp.CreateAsync(exportEnabled: false);
        var cash = await FindAsync(app, "Наличные");
        await using (var scope = app.Scope())
        {
            await scope.Get<AccountService>().SetHiddenAsync(cash.Id, true, Ct);
        }

        (await BalancesAsync(app)).Accounts.ShouldNotContain(b => b.Account.Name == "Наличные");
        await using var check = app.Scope();
        (await check.Get<AccountBalancesService>().LoadAsync(includeHidden: true, Ct)).Accounts
            .ShouldContain(b => b.Account.Name == "Наличные");
    }

    [Fact]
    public async Task Transfer_BetweenTheSameAccount_IsRejected()
    {
        await using var app = await TestApp.CreateAsync(exportEnabled: false);
        var card = await FindAsync(app, "Карта");

        await Should.ThrowAsync<DomainException>(() => TransferAsync(app, new TransferInput(Oct15, 1m, card.Id, card.Id, null)));
    }
}
