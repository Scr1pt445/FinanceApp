using FinanceApp.Application.Accounts;
using FinanceApp.Domain;

namespace FinanceApp.Presentation.Finance;

/// <summary>
/// A category against its monthly limit: «Еда», «18 400 ₽ из 30 000 ₽», «осталось 11 600 ₽» and a bar — accent
/// while there is room, orange from 80 %, the warning color when over.
/// </summary>
public sealed record LimitRow(string Title, string SpentText, string LeftText, double Progress, bool IsNear, bool IsOver)
{
    public static LimitRow From(LimitLine line, CurrencyCode currency)
    {
        ArgumentNullException.ThrowIfNull(line);
        var left = line.IsOver
            ? $"перерасход {Format.Money(line.Spent - line.Limit, currency)}"
            : $"осталось {Format.Money(line.Left, currency)}";
        return new LimitRow(
            line.Category.Name,
            $"{Format.Money(line.Spent, currency)} из {Format.Money(line.Limit, currency)}",
            left,
            (double)Math.Clamp(line.Share, 0, 1),
            line.IsNear && !line.IsOver,
            line.IsOver);
    }

    /// <summary>«Еда: 85 % лимита» / «Еда: лимит превышен на 2 000 ₽» — a line for the home screen.</summary>
    public static string Warning(LimitLine line, CurrencyCode currency)
    {
        ArgumentNullException.ThrowIfNull(line);
        return line.IsOver
            ? $"{line.Category.Name}: лимит превышен на {Format.Money(line.Spent - line.Limit, currency)}"
            : $"{line.Category.Name}: потрачено {decimal.Round(line.Share * 100, 0)} % лимита, осталось {Format.Money(line.Left, currency)}";
    }
}
