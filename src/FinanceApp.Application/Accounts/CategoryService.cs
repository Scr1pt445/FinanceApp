using FinanceApp.Application.Abstractions;
using FinanceApp.Domain;

namespace FinanceApp.Application.Accounts;

/// <summary>
/// User categories (requirements, section 3.5): add, rename, hide. Names are unique within income or
/// expense, case-insensitively. Categories are never deleted — hiding keeps the history intact.
/// </summary>
public sealed class CategoryService(ICategoryRepository categories, IUnitOfWork unitOfWork, DataChangedNotifier notifier)
{
    public async Task<Category> CreateAsync(string name, TransactionType type, CancellationToken cancellationToken = default)
    {
        var existing = await categories.ListAsync(cancellationToken);
        if (Find(existing, name, type, exceptId: null) is { } same)
        {
            // Creating a hidden one again simply brings it back.
            if (same.IsHidden)
            {
                same.Show();
                await SaveAsync(cancellationToken);
                return same;
            }

            throw Duplicate(name, type);
        }

        var sortOrder = existing.Count == 0 ? 0 : existing.Max(c => c.SortOrder) + 1;
        var category = new Category(Guid.CreateVersion7(), name, type, sortOrder);
        categories.Add(category);
        await SaveAsync(cancellationToken);
        return category;
    }

    public async Task<Category> RenameAsync(Guid id, string name, CancellationToken cancellationToken = default)
    {
        var category = await categories.FindAsync(id, cancellationToken) ?? throw NotFound();
        if (Find(await categories.ListAsync(cancellationToken), name, category.Type, exceptId: id) is not null)
        {
            throw Duplicate(name, category.Type);
        }

        category.Rename(name);
        await SaveAsync(cancellationToken);
        return category;
    }

    public async Task SetHiddenAsync(Guid id, bool hidden, CancellationToken cancellationToken = default)
    {
        var category = await categories.FindAsync(id, cancellationToken) ?? throw NotFound();
        if (hidden)
        {
            category.Hide();
        }
        else
        {
            category.Show();
        }

        await SaveAsync(cancellationToken);
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        await unitOfWork.SaveChangesAsync(cancellationToken);
        notifier.NotifyChanged();
    }

    private static Category? Find(IEnumerable<Category> existing, string name, TransactionType type, Guid? exceptId) =>
        existing.FirstOrDefault(c => c.Type == type && c.Id != exceptId
            && string.Equals(c.Name, name?.Trim(), StringComparison.CurrentCultureIgnoreCase));

    private static DomainException Duplicate(string name, TransactionType type) =>
        new($"Категория «{name?.Trim()}» уже есть среди {(type == TransactionType.Income ? "доходов" : "расходов")}.");

    private static DomainException NotFound() => new("Категория не найдена.");
}
