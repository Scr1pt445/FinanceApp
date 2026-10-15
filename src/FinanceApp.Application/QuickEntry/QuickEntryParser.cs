using System.Globalization;
using System.Text.RegularExpressions;
using FinanceApp.Domain;

namespace FinanceApp.Application.QuickEntry;

/// <summary>
/// Parses the quick-entry line (ADR 0009): <c>[+|&gt;]&lt;amount&gt; [category] [note…] [date] [@account]</c>.
/// Pure and fast — meant to run on every keystroke.
/// </summary>
public static partial class QuickEntryParser
{
    private const int MaxNameWords = 4;

    private static readonly Dictionary<string, DayOfWeek> Weekdays = new(StringComparer.OrdinalIgnoreCase)
    {
        ["пн"] = DayOfWeek.Monday, ["понедельник"] = DayOfWeek.Monday,
        ["вт"] = DayOfWeek.Tuesday, ["вторник"] = DayOfWeek.Tuesday,
        ["ср"] = DayOfWeek.Wednesday, ["среда"] = DayOfWeek.Wednesday, ["среду"] = DayOfWeek.Wednesday,
        ["чт"] = DayOfWeek.Thursday, ["четверг"] = DayOfWeek.Thursday,
        ["пт"] = DayOfWeek.Friday, ["пятница"] = DayOfWeek.Friday, ["пятницу"] = DayOfWeek.Friday,
        ["сб"] = DayOfWeek.Saturday, ["суббота"] = DayOfWeek.Saturday, ["субботу"] = DayOfWeek.Saturday,
        ["вс"] = DayOfWeek.Sunday, ["воскресенье"] = DayOfWeek.Sunday,
    };

    public static QuickEntryDraft Parse(string? input, QuickEntryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var issues = new List<QuickEntryIssue>();
        var tokens = (input ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();

        // Type prefix: attached ("+500") or standalone ("+ 500").
        var type = TransactionType.Expense;
        if (tokens.Count > 0 && tokens[0][0] is '+' or '>')
        {
            type = tokens[0][0] == '+' ? TransactionType.Income : TransactionType.Saving;
            tokens[0] = tokens[0][1..];
            if (tokens[0].Length == 0)
            {
                tokens.RemoveAt(0);
            }
        }

        var defaultAccount = context.Accounts.FirstOrDefault(a => a.Id == context.LastAccountId && !a.IsHidden)
            ?? context.Accounts.FirstOrDefault(a => !a.IsHidden);

        if (tokens.Count == 0)
        {
            issues.Add(new(QuickEntryIssueKind.EmptyInput, "Введите сумму, например: 350 еда кофе"));
            return new QuickEntryDraft(type, null, null, false, defaultAccount, true, context.Today, null, issues);
        }

        // Thousands written with spaces: "1 200" or "1 200 000,50". A bare number is never a category or a date.
        if (WholeNumberPattern().IsMatch(tokens[0]))
        {
            while (tokens.Count > 1 && ThousandsGroupPattern().IsMatch(tokens[1]) && !tokens[0].AsSpan().ContainsAny(".,"))
            {
                tokens[0] += tokens[1];
                tokens.RemoveAt(1);
            }
        }

        var amount = ParseAmount(tokens[0], issues);
        var rest = tokens.Skip(1).ToList();

        var account = defaultAccount;
        var accountIsDefault = true;
        var accountAt = rest.FindIndex(t => t.StartsWith('@'));
        if (accountAt >= 0)
        {
            accountIsDefault = false;
            account = MatchAccount(rest, accountAt, context.Accounts, issues);
        }
        else if (account is null)
        {
            issues.Add(new(QuickEntryIssueKind.NoAccount, "Нет ни одного счёта — добавьте счёт."));
        }

        var date = context.Today;
        for (var i = rest.Count - 1; i >= 0; i--)
        {
            if (TryParseDate(rest[i], context.Today, out var parsed))
            {
                date = parsed;
                rest.RemoveAt(i);
                break;
            }
        }

        var categories = context.Categories.Where(c => c.Type == type && !c.IsHidden).ToList();
        var (category, consumed, ambiguous) = MatchByName(rest, 0, categories, c => c.Name);
        rest.RemoveRange(0, consumed);

        var categoryFromHint = false;
        if (ambiguous.Count > 0)
        {
            issues.Add(new(QuickEntryIssueKind.AmbiguousCategory,
                $"Уточните категорию: {string.Join(", ", ambiguous.Select(c => c.Name))}"));
        }
        else if (category is null)
        {
            var hinted = context.Hints.Suggest(type, rest);
            category = categories.FirstOrDefault(c => c.Id == hinted);
            categoryFromHint = category is not null;
            if (category is null)
            {
                issues.Add(new(QuickEntryIssueKind.MissingCategory, "Укажите категорию после суммы, например: 350 еда"));
            }
        }

        var note = rest.Count == 0 ? null : string.Join(' ', rest);
        return new QuickEntryDraft(type, amount, category, categoryFromHint, account, accountIsDefault, date, note, issues);
    }

    private static decimal? ParseAmount(string token, List<QuickEntryIssue> issues)
    {
        var match = AmountPattern().Match(token);
        if (!match.Success
            || !decimal.TryParse(match.Value.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
            || amount <= 0
            || amount > Transaction.MaxAmount)
        {
            issues.Add(new(QuickEntryIssueKind.InvalidAmount, $"«{token}» — не сумма. Начните строку с суммы: 350 или 350,50"));
            return null;
        }

        if (match.Groups["fraction"].Length > 2)
        {
            issues.Add(new(QuickEntryIssueKind.InvalidAmount, "В сумме не больше двух знаков после запятой."));
            return null;
        }

        return amount;
    }

    private static Account? MatchAccount(List<string> rest, int at, IReadOnlyList<Account> accounts, List<QuickEntryIssue> issues)
    {
        var query = rest[at][1..];
        rest[at] = query;
        if (query.Length == 0)
        {
            rest.RemoveAt(at);
            issues.Add(new(QuickEntryIssueKind.UnknownAccount, "После @ напишите начало названия счёта, например: @нал"));
            return null;
        }

        var (account, consumed, ambiguous) = MatchByName(rest, at, accounts.Where(a => !a.IsHidden).ToList(), a => a.Name);
        if (account is not null)
        {
            rest.RemoveRange(at, consumed);
            return account;
        }

        rest.RemoveAt(at);
        issues.Add(ambiguous.Count > 0
            ? new(QuickEntryIssueKind.AmbiguousAccount, $"Уточните счёт: {string.Join(", ", ambiguous.Select(a => a.Name))}")
            : new(QuickEntryIssueKind.UnknownAccount, $"Счёт «{query}» не найден."));
        return null;
    }

    /// <summary>
    /// Matches one or more words starting at <paramref name="start"/> against names, longest phrase first.
    /// An exact name wins; otherwise a single prefix match. Several prefix matches of the first word are ambiguous.
    /// </summary>
    private static (T? Item, int Consumed, IReadOnlyList<T> Ambiguous) MatchByName<T>(
        List<string> words, int start, IReadOnlyList<T> items, Func<T, string> name)
        where T : class
    {
        IReadOnlyList<T> ambiguous = [];
        for (var count = Math.Min(MaxNameWords, words.Count - start); count >= 1; count--)
        {
            var phrase = string.Join(' ', words.Skip(start).Take(count));
            var exact = items.FirstOrDefault(i => string.Equals(name(i), phrase, StringComparison.CurrentCultureIgnoreCase));
            if (exact is not null)
            {
                return (exact, count, []);
            }

            var prefixed = items.Where(i => name(i).StartsWith(phrase, StringComparison.CurrentCultureIgnoreCase)).ToList();
            if (prefixed.Count == 1)
            {
                return (prefixed[0], count, []);
            }

            if (prefixed.Count > 1 && count == 1)
            {
                ambiguous = prefixed;
            }
        }

        return (null, 0, ambiguous);
    }

    private static bool TryParseDate(string token, DateOnly today, out DateOnly date)
    {
        date = today;
        switch (token.ToLowerInvariant())
        {
            case "сегодня":
                return true;
            case "вчера":
                date = today.AddDays(-1);
                return true;
            case "позавчера":
                date = today.AddDays(-2);
                return true;
        }

        if (Weekdays.TryGetValue(token, out var weekday))
        {
            date = today.AddDays(-(((int)today.DayOfWeek - (int)weekday + 7) % 7));
            return true;
        }

        var match = DatePattern().Match(token);
        if (!match.Success)
        {
            return false;
        }

        var day = int.Parse(match.Groups["day"].Value, CultureInfo.InvariantCulture);
        var month = int.Parse(match.Groups["month"].Value, CultureInfo.InvariantCulture);
        var year = match.Groups["year"].Success ? int.Parse(match.Groups["year"].Value, CultureInfo.InvariantCulture) : today.Year;
        if (year < 100)
        {
            year += 2000;
        }

        if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month))
        {
            return false;
        }

        date = new DateOnly(year, month, day);
        if (!match.Groups["year"].Success && date > today)
        {
            date = date.AddYears(-1);
        }

        return true;
    }

    [GeneratedRegex(@"^\d+(?:[.,](?<fraction>\d+))?$")]
    private static partial Regex AmountPattern();

    [GeneratedRegex(@"^\d{1,3}$")]
    private static partial Regex WholeNumberPattern();

    [GeneratedRegex(@"^\d{3}(?:[.,]\d+)?$")]
    private static partial Regex ThousandsGroupPattern();

    [GeneratedRegex(@"^(?<day>\d{1,2})\.(?<month>\d{1,2})(?:\.(?<year>\d{2}|\d{4}))?$")]
    private static partial Regex DatePattern();
}
