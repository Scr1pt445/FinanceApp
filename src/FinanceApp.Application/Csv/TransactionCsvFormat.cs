using System.Globalization;
using FinanceApp.Domain;

namespace FinanceApp.Application.Csv;

/// <summary>
/// The CSV contract with Obsidian (requirements, section 5) and its extension (ADR 0008).
/// Changing names or order of the first six columns breaks the Obsidian page.
/// </summary>
public static class TransactionCsvFormat
{
    public const string Date = "date";
    public const string Type = "type";
    public const string Amount = "amount";
    public const string Category = "category";
    public const string Account = "account";
    public const string Note = "note";
    public const string Currency = "currency";
    public const string OriginalAmount = "original_amount";
    public const string Id = "id";
    public const string ToAccount = "to_account";
    public const string ToAmount = "to_amount";

    /// <summary>Exported columns, in order (format v2, ADR 0011). The first six are the original contract.</summary>
    public static IReadOnlyList<string> Columns { get; } =
        [Date, Type, Amount, Category, Account, Note, Currency, OriginalAmount, Id, ToAccount, ToAmount];

    public static IReadOnlyList<string> RequiredColumns { get; } = [Date, Type, Amount];

    private const string DateFormat = "yyyy-MM-dd";

    public static string FormatDate(DateOnly date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);

    public static bool TryParseDate(string value, out DateOnly date) =>
        DateOnly.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    /// <summary>Whole numbers without a fractional part (<c>350</c>), others with two digits (<c>350.50</c>).</summary>
    public static string FormatAmount(decimal amount)
    {
        var rounded = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        return rounded == decimal.Truncate(rounded)
            ? rounded.ToString("0", CultureInfo.InvariantCulture)
            : rounded.ToString("0.00", CultureInfo.InvariantCulture);
    }

    /// <summary>Accepts only plain positive numbers with a dot: no signs, spaces, thousands separators or exponent.</summary>
    public static bool TryParseAmount(string value, out decimal amount) =>
        decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount);

    public static string FormatType(TransactionType type) =>
        type switch
        {
            TransactionType.Income => "income",
            TransactionType.Expense => "expense",
            TransactionType.Transfer => "transfer",
            TransactionType.Saving => "saving",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        };

    /// <summary>
    /// Exactly <c>income</c>, <c>expense</c> or <c>transfer</c>; legacy <c>saving</c> (format v1) is accepted
    /// on import. Case-sensitive, as the contract says.
    /// </summary>
    public static bool TryParseType(string value, out TransactionType type)
    {
        (var ok, type) = value switch
        {
            "income" => (true, TransactionType.Income),
            "expense" => (true, TransactionType.Expense),
            "transfer" => (true, TransactionType.Transfer),
            "saving" => (true, TransactionType.Saving),
            _ => (false, default),
        };
        return ok;
    }
}
