using FinanceApp.Application.Abstractions;
using FinanceApp.Domain;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Infrastructure.Persistence;

internal sealed class EfTransactionRepository(FinanceDbContext db) : ITransactionRepository
{
    public Task<Transaction?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Transactions.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Transaction>> ListAsync(DateOnly? from = null, DateOnly? to = null, CancellationToken cancellationToken = default)
    {
        var query = db.Transactions.AsQueryable();
        if (from is { } start)
        {
            query = query.Where(t => t.Date >= start);
        }

        if (to is { } end)
        {
            query = query.Where(t => t.Date <= end);
        }

        return await query.OrderBy(t => t.Date).ThenBy(t => t.CreatedAt).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlySet<Guid>> ExistingIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var wanted = ids.Distinct().ToList();
        var existing = new HashSet<Guid>();

        // Stay well below SQLite's limit on query parameters.
        foreach (var chunk in wanted.Chunk(500))
        {
            existing.UnionWith(await db.Transactions.Where(t => chunk.Contains(t.Id)).Select(t => t.Id).ToListAsync(cancellationToken));
        }

        return existing;
    }

    public void Add(Transaction transaction) => db.Transactions.Add(transaction);

    public void Remove(Transaction transaction) => db.Transactions.Remove(transaction);
}

internal sealed class EfCategoryRepository(FinanceDbContext db) : ICategoryRepository
{
    public async Task<IReadOnlyList<Category>> ListAsync(CancellationToken cancellationToken = default) =>
        await db.Categories.OrderBy(c => c.Type).ThenBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(cancellationToken);

    public Task<Category?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Categories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public void Add(Category category) => db.Categories.Add(category);
}

internal sealed class EfAccountRepository(FinanceDbContext db) : IAccountRepository
{
    public async Task<IReadOnlyList<Account>> ListAsync(CancellationToken cancellationToken = default) =>
        await db.Accounts.OrderBy(a => a.SortOrder).ThenBy(a => a.Name).ToListAsync(cancellationToken);

    public Task<Account?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Accounts.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public void Add(Account account) => db.Accounts.Add(account);
}

internal sealed class EfRecurringOperationRepository(FinanceDbContext db) : IRecurringOperationRepository
{
    public async Task<IReadOnlyList<RecurringOperation>> ListAsync(CancellationToken cancellationToken = default) =>
        await db.RecurringOperations.OrderBy(r => r.NextDate).ThenBy(r => r.CreatedAt).ToListAsync(cancellationToken);

    public Task<RecurringOperation?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.RecurringOperations.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public void Add(RecurringOperation operation) => db.RecurringOperations.Add(operation);

    public void Remove(RecurringOperation operation) => db.RecurringOperations.Remove(operation);
}

internal sealed class EfDebtRepository(FinanceDbContext db) : IDebtRepository
{
    public async Task<IReadOnlyList<Debt>> ListAsync(CancellationToken cancellationToken = default) =>
        (await db.Debts.ToListAsync(cancellationToken))
            .OrderBy(d => d.IsClosed)
            .ThenBy(d => d.IsClosed ? DateOnly.MaxValue : d.DueDate ?? DateOnly.MaxValue)
            .ThenByDescending(d => d.ClosedOn)
            .ThenByDescending(d => d.Date)
            .ThenByDescending(d => d.CreatedAt)
            .ToList();

    public Task<Debt?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Debts.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

    public void Add(Debt debt) => db.Debts.Add(debt);

    public void Remove(Debt debt) => db.Debts.Remove(debt);
}

internal sealed class EfUnitOfWork(FinanceDbContext db) : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => db.SaveChangesAsync(cancellationToken);

    public async Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var result = await work(cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }
}
