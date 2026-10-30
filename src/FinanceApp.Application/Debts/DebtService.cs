using FinanceApp.Application.Abstractions;
using FinanceApp.Domain;

namespace FinanceApp.Application.Debts;

/// <summary>What a debt is made of.</summary>
public sealed record DebtInput(
    DebtDirection Direction,
    string Person,
    decimal Amount,
    CurrencyCode Currency,
    DateOnly Date,
    DateOnly? DueDate,
    string? Note);

/// <summary>What is still owed in one currency: «мне должны 15 000 ₽», «я должен 200 $».</summary>
public sealed record DebtTotal(DebtDirection Direction, CurrencyCode Currency, decimal Left);

/// <summary>
/// «Долги»: who owes the user and whom the user owes. A notebook apart from the accounts — debts change no balance
/// and no summary; they only remind what should come back and when.
/// </summary>
public sealed class DebtService(IDebtRepository debts, IUnitOfWork unitOfWork, TimeProvider timeProvider)
{
    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    private DateOnly Today => DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);

    /// <summary>The open ones first (the soonest due on top), then the closed ones, the latest first.</summary>
    public Task<IReadOnlyList<Debt>> ListAsync(CancellationToken cancellationToken = default) => debts.ListAsync(cancellationToken);

    public async Task<Debt> AddAsync(DebtInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var debt = Debt.Create(input.Direction, input.Person, input.Amount, input.Currency, input.Date, input.DueDate, input.Note, Now);
        debts.Add(debt);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return debt;
    }

    public async Task<Debt> UpdateAsync(Guid id, DebtInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var debt = await debts.FindAsync(id, cancellationToken) ?? throw NotFound();
        debt.Update(input.Direction, input.Person, input.Amount, input.Currency, input.Date, input.DueDate, input.Note);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return debt;
    }

    /// <summary>Part or all of it came back today; the whole sum closes the debt.</summary>
    public async Task<Debt> RepayAsync(Guid id, decimal amount, CancellationToken cancellationToken = default)
    {
        var debt = await debts.FindAsync(id, cancellationToken) ?? throw NotFound();
        debt.Repay(amount, Today);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return debt;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var debt = await debts.FindAsync(id, cancellationToken) ?? throw NotFound();
        debts.Remove(debt);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>What is still owed, per direction and currency; currencies are not mixed — no rate is needed.</summary>
    public static IReadOnlyList<DebtTotal> Totals(IEnumerable<Debt> all)
    {
        ArgumentNullException.ThrowIfNull(all);
        return all
            .Where(d => !d.IsClosed)
            .GroupBy(d => (d.Direction, d.Currency))
            .Select(g => new DebtTotal(g.Key.Direction, g.Key.Currency, g.Sum(d => d.Left)))
            .OrderBy(t => t.Direction)
            .ThenByDescending(t => t.Left)
            .ToList();
    }

    private static DomainException NotFound() => new("Долг не найден.");
}
