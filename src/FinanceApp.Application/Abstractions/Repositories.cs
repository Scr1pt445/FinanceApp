using FinanceApp.Domain;

namespace FinanceApp.Application.Abstractions;

public interface ITransactionRepository
{
    Task<Transaction?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Transactions with <paramref name="from"/> ≤ date ≤ <paramref name="to"/>; all when both are null.</summary>
    Task<IReadOnlyList<Transaction>> ListAsync(DateOnly? from = null, DateOnly? to = null, CancellationToken cancellationToken = default);

    Task<IReadOnlySet<Guid>> ExistingIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);

    void Add(Transaction transaction);

    void Remove(Transaction transaction);
}

public interface ICategoryRepository
{
    Task<IReadOnlyList<Category>> ListAsync(CancellationToken cancellationToken = default);

    Task<Category?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(Category category);
}

public interface IAccountRepository
{
    Task<IReadOnlyList<Account>> ListAsync(CancellationToken cancellationToken = default);

    Task<Account?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(Account account);
}

/// <summary>Commits the changes made through the repositories as one unit.</summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Runs <paramref name="work"/> in a database transaction; nothing is saved if it throws.</summary>
    Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken = default);
}
