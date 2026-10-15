using FinanceApp.Domain;

namespace FinanceApp.Application.Csv;

/// <summary>A row that could not be read, with a user-facing reason.</summary>
public sealed record CsvRowError(int LineNumber, string Message)
{
    public override string ToString() => $"Строка {LineNumber}: {Message}";
}

public sealed record CsvReadResult(IReadOnlyList<TransactionCsvRow> Rows, IReadOnlyList<CsvRowError> Errors)
{
    public bool HasErrors => Errors.Count > 0;
}

/// <summary>
/// Reads the transactions CSV. Columns are matched by name, so order does not matter and unknown
/// columns are ignored. Accepts both the original 6-column contract and the extended export.
/// </summary>
public static class TransactionCsvReader
{
    public static CsvReadResult Read(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return ReadText(reader.ReadToEnd());
    }

    public static CsvReadResult ReadText(string text)
    {
        var rows = new List<TransactionCsvRow>();
        var errors = new List<CsvRowError>();

        IReadOnlyList<CsvRecord> records;
        try
        {
            records = CsvParser.ParseText(text).ToList();
        }
        catch (CsvFormatException ex)
        {
            return new CsvReadResult(rows, [new CsvRowError(ex.LineNumber, ex.Message)]);
        }

        if (records.Count == 0)
        {
            return new CsvReadResult(rows, [new CsvRowError(1, "файл пустой, нет строки заголовка")]);
        }

        var header = records[0];
        var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < header.Fields.Count; i++)
        {
            columns.TryAdd(header.Fields[i].Trim(), i);
        }

        var missing = TransactionCsvFormat.RequiredColumns.Where(c => !columns.ContainsKey(c)).ToList();
        if (missing.Count > 0)
        {
            return new CsvReadResult(
                rows,
                [new CsvRowError(header.LineNumber, $"в заголовке нет колонок: {string.Join(", ", missing)}")]);
        }

        foreach (var record in records.Skip(1))
        {
            if (TryReadRow(record, columns, out var row, out var error))
            {
                rows.Add(row);
            }
            else
            {
                errors.Add(new CsvRowError(record.LineNumber, error));
            }
        }

        return new CsvReadResult(rows, errors);
    }

    private static bool TryReadRow(
        CsvRecord record,
        Dictionary<string, int> columns,
        out TransactionCsvRow row,
        out string error)
    {
        row = null!;
        error = string.Empty;

        string? Field(string name) =>
            columns.TryGetValue(name, out var index) && index < record.Fields.Count
                ? NullIfEmpty(record.Fields[index].Trim())
                : null;

        var dateText = Field(TransactionCsvFormat.Date);
        if (dateText is null || !TransactionCsvFormat.TryParseDate(dateText, out var date))
        {
            error = $"некорректная дата «{dateText}», ожидается ГГГГ-ММ-ДД";
            return false;
        }

        var typeText = Field(TransactionCsvFormat.Type);
        if (typeText is null || !TransactionCsvFormat.TryParseType(typeText, out var type))
        {
            error = $"некорректный тип «{typeText}», ожидается income, expense или transfer";
            return false;
        }

        if (!TryReadPositiveAmount(Field(TransactionCsvFormat.Amount), "amount", out var amount, out error))
        {
            return false;
        }

        CurrencyCode? currency = null;
        var currencyText = Field(TransactionCsvFormat.Currency);
        if (currencyText is not null)
        {
            if (!CurrencyCode.TryParse(currencyText, out var parsed))
            {
                error = $"некорректная валюта «{currencyText}»";
                return false;
            }

            currency = parsed;
        }

        decimal? originalAmount = null;
        var originalText = Field(TransactionCsvFormat.OriginalAmount);
        if (originalText is not null)
        {
            if (currency is null)
            {
                error = "указана original_amount, но не указана currency";
                return false;
            }

            if (!TryReadPositiveAmount(originalText, "original_amount", out var original, out error))
            {
                return false;
            }

            originalAmount = original;
        }

        Guid? id = null;
        var idText = Field(TransactionCsvFormat.Id);
        if (idText is not null)
        {
            if (!Guid.TryParse(idText, out var parsedId) || parsedId == Guid.Empty)
            {
                error = $"некорректный id «{idText}»";
                return false;
            }

            id = parsedId;
        }

        var toAccount = Field(TransactionCsvFormat.ToAccount);
        decimal? toAmount = null;
        if (type == TransactionType.Transfer)
        {
            if (toAccount is null)
            {
                error = "у перевода не указан to_account";
                return false;
            }

            if (Field(TransactionCsvFormat.ToAmount) is { } toAmountText)
            {
                if (!TryReadPositiveAmount(toAmountText, "to_amount", out var parsedToAmount, out error))
                {
                    return false;
                }

                toAmount = parsedToAmount;
            }
        }

        row = new TransactionCsvRow(
            date,
            type,
            amount,
            Field(TransactionCsvFormat.Category),
            Field(TransactionCsvFormat.Account),
            Field(TransactionCsvFormat.Note),
            currency,
            originalAmount,
            id,
            record.LineNumber,
            type == TransactionType.Transfer ? toAccount : null,
            toAmount);
        return true;
    }

    private static bool TryReadPositiveAmount(string? text, string column, out decimal amount, out string error)
    {
        error = string.Empty;
        if (text is null || !TransactionCsvFormat.TryParseAmount(text, out amount) || amount <= 0)
        {
            amount = 0;
            error = $"некорректная сумма {column} «{text}», ожидается положительное число с точкой, например 350.50";
            return false;
        }

        if (decimal.Round(amount, 2) != amount)
        {
            error = $"в сумме {column} «{text}» больше двух знаков после точки";
            return false;
        }

        return true;
    }

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;
}
