using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using FinanceApp.Application.Transactions;
using FinanceApp.Domain;

namespace FinanceApp.Presentation.Main;

/// <summary>
/// The edit panel of one transaction. Offers visible categories of the chosen type and visible accounts
/// (plus the current ones even if hidden), validates the amount as the user types.
/// </summary>
public sealed partial class TransactionEditorViewModel : ObservableObject
{
    private readonly IReadOnlyList<Category> _allCategories;

    public TransactionEditorViewModel(Transaction transaction, IReadOnlyList<Category> categories, IReadOnlyList<Account> accounts)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(accounts);

        Transaction = transaction;
        _allCategories = categories.Where(c => !c.IsHidden || c.Id == transaction.CategoryId).ToList();
        Accounts = accounts.Where(a => !a.IsHidden || a.Id == transaction.AccountId).ToList();

        Date = transaction.Date.ToDateTime(TimeOnly.MinValue);
        Type = transaction.Type;
        AmountText = transaction.Amount.ToString("0.##", CultureInfo.GetCultureInfo("ru-RU"));
        Account = Accounts.FirstOrDefault(a => a.Id == transaction.AccountId);
        Category = Categories.FirstOrDefault(c => c.Id == transaction.CategoryId);
        Note = transaction.Note ?? string.Empty;
    }

    public Transaction Transaction { get; }

    public IReadOnlyList<TransactionType> Types { get; } = [TransactionType.Expense, TransactionType.Income];

    public IReadOnlyList<Account> Accounts { get; }

    public IReadOnlyList<Category> Categories => _allCategories.Where(c => c.Type == Type).ToList();

    /// <summary><see cref="DateTime"/> for the WPF DatePicker; only the date part is used.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Problem), nameof(CanSave))]
    public partial DateTime? Date { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Categories), nameof(Problem), nameof(CanSave))]
    public partial TransactionType Type { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Problem), nameof(CanSave))]
    public partial string AmountText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Problem), nameof(CanSave))]
    public partial Category? Category { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Problem), nameof(CanSave))]
    public partial Account? Account { get; set; }

    [ObservableProperty]
    public partial string Note { get; set; }

    /// <summary>What is wrong with the form, or <c>null</c>.</summary>
    public string? Problem =>
        Date is null ? "Укажите дату."
        : ParseAmount() is null ? "Сумма — положительное число, не больше двух знаков после запятой."
        : Category is null ? "Выберите категорию."
        : Account is null ? "Выберите счёт."
        : null;

    public bool CanSave => Problem is null;

    public TransactionInput ToInput() =>
        CanSave
            ? new TransactionInput(DateOnly.FromDateTime(Date!.Value), Type, ParseAmount()!.Value, Category!.Id, Account!.Id, Note)
            : throw new InvalidOperationException(Problem);

    partial void OnTypeChanged(TransactionType value)
    {
        if (Category?.Type != value)
        {
            Category = Categories.FirstOrDefault();
        }
    }

    private decimal? ParseAmount()
    {
        var text = AmountText.Replace(" ", string.Empty, StringComparison.Ordinal).Replace(' '.ToString(), string.Empty, StringComparison.Ordinal).Replace(',', '.');
        return decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
               && amount > 0 && amount <= Domain.Transaction.MaxAmount && decimal.Round(amount, 2) == amount
            ? amount
            : null;
    }
}
