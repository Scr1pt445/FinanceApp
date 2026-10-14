using System.Text;

namespace FinanceApp.Application.Csv;

/// <summary>One parsed CSV record and the 1-based line where it starts.</summary>
public sealed record CsvRecord(int LineNumber, IReadOnlyList<string> Fields);

/// <summary>
/// Minimal RFC 4180 parser: comma separator, double-quoted fields with doubled quotes inside,
/// line breaks inside quoted fields, CRLF or LF. Blank lines are skipped, a leading BOM is ignored.
/// </summary>
public static class CsvParser
{
    public static IEnumerable<CsvRecord> Parse(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return ParseText(reader.ReadToEnd());
    }

    public static IEnumerable<CsvRecord> ParseText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var position = text.Length > 0 && text[0] == '﻿' ? 1 : 0;
        var line = 1;
        var fields = new List<string>();
        var field = new StringBuilder();
        var recordLine = line;
        var inQuotes = false;
        var fieldWasQuoted = false;
        var recordHasContent = false;

        while (position < text.Length)
        {
            var c = text[position];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (position + 1 < text.Length && text[position + 1] == '"')
                    {
                        field.Append('"');
                        position += 2;
                        continue;
                    }

                    inQuotes = false;
                    position++;
                    if (position < text.Length && text[position] is not (',' or '\r' or '\n'))
                    {
                        throw new CsvFormatException(line, "после закрывающей кавычки ожидается запятая или конец строки");
                    }

                    continue;
                }

                if (c == '\n')
                {
                    line++;
                }

                field.Append(c);
                position++;
                continue;
            }

            switch (c)
            {
                case '"' when field.Length == 0 && !fieldWasQuoted:
                    inQuotes = true;
                    fieldWasQuoted = true;
                    recordHasContent = true;
                    position++;
                    break;

                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    fieldWasQuoted = false;
                    recordHasContent = true;
                    position++;
                    break;

                case '\r' or '\n':
                    if (recordHasContent || field.Length > 0)
                    {
                        fields.Add(field.ToString());
                        yield return new CsvRecord(recordLine, fields.ToArray());
                    }

                    fields.Clear();
                    field.Clear();
                    fieldWasQuoted = false;
                    recordHasContent = false;
                    position += c == '\r' && position + 1 < text.Length && text[position + 1] == '\n' ? 2 : 1;
                    line++;
                    recordLine = line;
                    break;

                default:
                    field.Append(c);
                    recordHasContent = true;
                    position++;
                    break;
            }
        }

        if (inQuotes)
        {
            throw new CsvFormatException(recordLine, "не закрыта кавычка");
        }

        if (recordHasContent || field.Length > 0)
        {
            fields.Add(field.ToString());
            yield return new CsvRecord(recordLine, fields.ToArray());
        }
    }
}

/// <summary>The file is not valid CSV at all (as opposed to a row with a bad value).</summary>
public sealed class CsvFormatException : Exception
{
    public CsvFormatException(int lineNumber, string reason)
        : base($"Строка {lineNumber}: {reason}.")
    {
        LineNumber = lineNumber;
    }

    public CsvFormatException()
    {
    }

    public CsvFormatException(string message)
        : base(message)
    {
    }

    public CsvFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public int LineNumber { get; }
}
