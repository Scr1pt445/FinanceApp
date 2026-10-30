using FinanceApp.Application.Accounts;
using FinanceApp.Application.Transactions;
using FinanceApp.Domain;

namespace FinanceApp.Infrastructure.Tests;

/// <summary>A card in rubles, a receipt in dollars: the amount is converted, the receipt stays in the note.</summary>
public sealed class PaidInAnotherCurrencyTests : IAsyncLifetime
{
    private TestApp _app = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateOnly Day = new(2026, 10, 20);

    public async ValueTask InitializeAsync() => _app = await TestApp.CreateAsync(exportEnabled: false);

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    private async Task<(Guid Card, Guid Food)> CatalogAsync()
    {
        await using var scope = _app.Scope();
        var catalog = await scope.Get<CatalogService>().LoadAsync(Ct);
        return (catalog.Accounts.Single(a => a.Name == "Карта").Id, catalog.Categories.Single(c => c.Name == "Еда" && c.Type == TransactionType.Expense).Id);
    }

    [Fact]
    public async Task ReceiptInDollars_IsChargedInRubles_AtTheCbrRate()
    {
        var (card, food) = await CatalogAsync();
        await using var scope = _app.Scope();

        var saved = await scope.Get<TransactionService>().AddAsync(
            new TransactionInput(Day, TransactionType.Expense, 20m, food, card, "ужин", PaidCurrency: CurrencyCode.Usd), Ct);

        saved.Transaction.Amount.ShouldBe(1800m);
        saved.Transaction.Currency.ShouldBe(CurrencyCode.Rub);
        saved.Transaction.Note.ShouldBe("ужин · 20 USD");
    }

    [Fact]
    public async Task WhatTheBankCharged_WinsOverTheCbrRate()
    {
        var (card, food) = await CatalogAsync();
        await using var scope = _app.Scope();

        var saved = await scope.Get<TransactionService>().AddAsync(
            new TransactionInput(Day, TransactionType.Expense, 12.5m, food, card, null, PaidCurrency: CurrencyCode.Usd, PaidRate: 92.4m), Ct);

        saved.Transaction.Amount.ShouldBe(1155m);
        saved.Transaction.Note.ShouldBe("12,5 USD");
        _app.Rates.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task SameCurrencyAsTheAccount_ChangesNothing()
    {
        var (card, food) = await CatalogAsync();
        await using var scope = _app.Scope();

        var saved = await scope.Get<TransactionService>().AddAsync(
            new TransactionInput(Day, TransactionType.Expense, 350m, food, card, "кофе", PaidCurrency: CurrencyCode.Rub), Ct);

        saved.Transaction.Amount.ShouldBe(350m);
        saved.Transaction.Note.ShouldBe("кофе");
    }
}
