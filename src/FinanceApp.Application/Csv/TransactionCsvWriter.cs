using System.Text;

namespace FinanceApp.Application.Csv;

/// <summary>Writes rows in the contract format: UTF-8 without BOM, LF line endings, RFC 4180 quoting.</summary>
public static class TransactionCsvWriter
{
    /// <summary>UTF-8 without BOM — Obsidian would otherwise see the BOM as part of the first column name.</summary>
    public static readonly Encoding Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private const string NewLine = "\n";

    public static void Write(IEnumerable<TransactionCsvRow> rows, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(writer);

        writer.Write(string.Join(',', TransactionCsvFormat.Columns));
        writer.Write(NewLine);

        foreach (var row in rows)
        {
            writer.Write(TransactionCsvFormat.FormatDate(row.Date));
            writer.Write(',');
            writer.Write(TransactionCsvFormat.FormatType(row.Type));
            writer.Write(',');
            writer.Write(TransactionCsvFormat.FormatAmount(row.Amount));
            writer.Write(',');
            writer.Write(Escape(row.Category));
            writer.Write(',');
            writer.Write(Escape(row.Account));
            writer.Write(',');
            writer.Write(Escape(row.Note));
            writer.Write(',');
            writer.Write(row.Currency?.Value);
            writer.Write(',');
            writer.Write(row.OriginalAmount is { } original ? TransactionCsvFormat.FormatAmount(original) : null);
            writer.Write(',');
            writer.Write(row.Id?.ToString("D"));
            writer.Write(NewLine);
        }
    }

    public static byte[] WriteToBytes(IEnumerable<TransactionCsvRow> rows)
    {
        using var stream = new MemoryStream();
        using (var writer = new StreamWriter(stream, Encoding, leaveOpen: true))
        {
            Write(rows, writer);
        }

        return stream.ToArray();
    }

    /// <summary>Quotes a field that contains a comma, a quote or a line break; inner quotes are doubled.</summary>
    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.AsSpan().IndexOfAny(",\"\r\n") >= 0
            ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : value;
    }
}
