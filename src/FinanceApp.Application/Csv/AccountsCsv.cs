using System.Globalization;
using System.Text;
using FinanceApp.Domain;

namespace FinanceApp.Application.Csv;

/// <summary>One line of <c>accounts.csv</c> (ADR 0011). <see cref="Balance"/> is in <see cref="Currency"/>.</summary>
public sealed record AccountCsvRow(
    string Name,
    AccountKind Kind,
    CurrencyCode Currency,
    decimal Balance,
    bool Hidden,
    decimal? GoalAmount,
    DateOnly? GoalDate);

/// <summary>
/// <c>accounts.csv</c>: accounts and envelopes with balances, written next to <c>transactions.csv</c>.
/// Same rules as the transactions file: UTF-8 without BOM, LF, RFC 4180 quoting, invariant numbers.
/// </summary>
public static class AccountsCsv
{
    public const string FileName = "accounts.csv";

    public static IReadOnlyList<string> Columns { get; } = ["name", "kind", "currency", "balance", "hidden", "goal_amount", "goal_date"];

    public static AccountCsvRow Row(Account account, decimal balance)
    {
        ArgumentNullException.ThrowIfNull(account);
        return new AccountCsvRow(account.Name, account.Kind, account.Currency, balance, account.IsHidden, account.GoalAmount, account.GoalDate);
    }

    public static byte[] WriteToBytes(IEnumerable<AccountCsvRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var text = new StringBuilder();
        text.Append(string.Join(',', Columns)).Append('\n');
        foreach (var row in rows)
        {
            text.Append(TransactionCsvWriter.Escape(row.Name)).Append(',')
                .Append(row.Kind == AccountKind.Envelope ? "envelope" : "account").Append(',')
                .Append(row.Currency.Value).Append(',')
                .Append(FormatSigned(row.Balance)).Append(',')
                .Append(row.Hidden ? "true" : "false").Append(',')
                .Append(row.GoalAmount is { } goal ? TransactionCsvFormat.FormatAmount(goal) : null).Append(',')
                .Append(row.GoalDate is { } date ? TransactionCsvFormat.FormatDate(date) : null)
                .Append('\n');
        }

        return TransactionCsvWriter.Encoding.GetBytes(text.ToString());
    }

    /// <summary>Reads what the import needs: names, kinds, currencies and goals. Bad rows are skipped.</summary>
    public static IReadOnlyList<AccountCsvRow> Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var records = CsvParser.ParseText(text).ToList();
        if (records.Count == 0)
        {
            return [];
        }

        var columns = records[0].Fields
            .Select((name, index) => (name: name.Trim(), index))
            .GroupBy(c => c.name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().index, StringComparer.OrdinalIgnoreCase);

        var rows = new List<AccountCsvRow>();
        foreach (var record in records.Skip(1))
        {
            string? Field(string name) =>
                columns.TryGetValue(name, out var i) && i < record.Fields.Count && record.Fields[i].Trim().Length > 0
                    ? record.Fields[i].Trim()
                    : null;

            if (Field("name") is not { } name || !CurrencyCode.TryParse(Field("currency") ?? "RUB", out var currency))
            {
                continue;
            }

            var kind = string.Equals(Field("kind"), "envelope", StringComparison.OrdinalIgnoreCase) ? AccountKind.Envelope : AccountKind.Regular;
            decimal? goal = Field("goal_amount") is { } goalText && TransactionCsvFormat.TryParseAmount(goalText, out var g) && g > 0 ? g : null;
            DateOnly? goalDate = Field("goal_date") is { } dateText && TransactionCsvFormat.TryParseDate(dateText, out var d) ? d : null;
            _ = decimal.TryParse(Field("balance"), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var balance);

            rows.Add(new AccountCsvRow(name, kind, currency, balance, Field("hidden") == "true", goal, goalDate));
        }

        return rows;
    }

    private static string FormatSigned(decimal amount) =>
        amount < 0 ? "-" + TransactionCsvFormat.FormatAmount(-amount) : TransactionCsvFormat.FormatAmount(amount);
}
