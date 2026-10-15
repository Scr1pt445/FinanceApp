using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FinanceApp.Domain;

namespace FinanceApp.Presentation.Finance;

/// <summary>A category row: rename in place, hide or show again, how often it is used.</summary>
public sealed partial class CategoryItem(Category category, int usage, Func<CategoryItem, Task> rename, Func<CategoryItem, Task> toggleHidden) : ObservableObject
{
    public Category Category { get; } = category;

    public string UsageText { get; } = usage == 0 ? "не использовалась" : $"операций: {usage}";

    public bool IsHidden => Category.IsHidden;

    [ObservableProperty]
    public partial string Name { get; set; } = category.Name;

    [ObservableProperty]
    public partial bool IsEditing { get; set; }

    [RelayCommand]
    private void StartRename() => IsEditing = true;

    [RelayCommand]
    private Task FinishRenameAsync() => rename(this);

    [RelayCommand]
    private void CancelRename()
    {
        Name = Category.Name;
        IsEditing = false;
    }

    [RelayCommand]
    private Task ToggleHiddenAsync() => toggleHidden(this);
}

/// <summary>«Категории» page (requirements, section 3.5): add, rename and hide expense and income categories.</summary>
public sealed partial class CategoriesViewModel(IFinanceBackend backend) : ObservableObject
{
    public ObservableCollection<CategoryItem> Expenses { get; } = [];

    public ObservableCollection<CategoryItem> Incomes { get; } = [];

    [ObservableProperty]
    public partial string NewExpenseName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewIncomeName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool ShowHidden { get; set; }

    [ObservableProperty]
    public partial string Status { get; private set; } = string.Empty;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var catalog = await backend.LoadCatalogAsync(cancellationToken);
            Fill(Expenses, catalog.Categories, TransactionType.Expense, catalog.CategoryUsage);
            Fill(Incomes, catalog.Categories, TransactionType.Income, catalog.CategoryUsage);
        }
#pragma warning disable CA1031 // Shown on the page.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            Status = ex.Message;
        }
    }

    [RelayCommand]
    private Task AddExpenseAsync() => AddAsync(NewExpenseName, TransactionType.Expense, () => NewExpenseName = string.Empty);

    [RelayCommand]
    private Task AddIncomeAsync() => AddAsync(NewIncomeName, TransactionType.Income, () => NewIncomeName = string.Empty);

    private async Task RenameAsync(CategoryItem item)
    {
        item.IsEditing = false;
        if (string.IsNullOrWhiteSpace(item.Name) || item.Name.Trim() == item.Category.Name)
        {
            item.Name = item.Category.Name;
            return;
        }

        await RunAsync(() => backend.RenameCategoryAsync(item.Category.Id, item.Name));
    }

    private Task ToggleHiddenAsync(CategoryItem item) =>
        RunAsync(() => backend.SetCategoryHiddenAsync(item.Category.Id, !item.Category.IsHidden));

    partial void OnShowHiddenChanged(bool value) => _ = LoadAsync();

    private async Task AddAsync(string name, TransactionType type, Action clear)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            Status = "Введите название категории.";
            return;
        }

        await RunAsync(async () =>
        {
            await backend.CreateCategoryAsync(name, type);
            clear();
        });
    }

    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
            Status = string.Empty;
        }
#pragma warning disable CA1031 // Duplicates and the like are shown on the page.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            Status = ex.Message;
        }

        await LoadAsync();
    }

    private void Fill(ObservableCollection<CategoryItem> target, IEnumerable<Category> categories, TransactionType type, IReadOnlyDictionary<Guid, int> usage)
    {
        target.Clear();
        foreach (var category in categories
            .Where(c => c.Type == type && (ShowHidden || !c.IsHidden))
            .OrderBy(c => c.IsHidden)
            .ThenByDescending(c => usage.GetValueOrDefault(c.Id))
            .ThenBy(c => c.SortOrder))
        {
            target.Add(new CategoryItem(category, usage.GetValueOrDefault(category.Id), RenameAsync, ToggleHiddenAsync));
        }
    }
}
