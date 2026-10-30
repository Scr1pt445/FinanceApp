using FinanceApp.Domain;

namespace FinanceApp.Presentation.Finance;

/// <summary>Currencies the app knows by name — those the Central Bank of Russia publishes, the closest first.</summary>
public static class CurrencyNames
{
    public static IReadOnlyList<(CurrencyCode Code, string Name)> Known { get; } =
    [
        (CurrencyCode.Rub, "Российский рубль"),
        (CurrencyCode.Usd, "Доллар США"),
        (CurrencyCode.Eur, "Евро"),
        (CurrencyCode.Byn, "Белорусский рубль"),
        (Code("KZT"), "Казахстанский тенге"),
        (Code("UAH"), "Украинская гривна"),
        (Code("CNY"), "Китайский юань"),
        (Code("TRY"), "Турецкая лира"),
        (Code("GEL"), "Грузинский лари"),
        (Code("AMD"), "Армянский драм"),
        (Code("AZN"), "Азербайджанский манат"),
        (Code("KGS"), "Киргизский сом"),
        (Code("UZS"), "Узбекский сум"),
        (Code("TJS"), "Таджикский сомони"),
        (Code("MDL"), "Молдавский лей"),
        (Code("GBP"), "Фунт стерлингов"),
        (Code("PLN"), "Польский злотый"),
        (Code("CZK"), "Чешская крона"),
        (Code("CHF"), "Швейцарский франк"),
        (Code("AED"), "Дирхам ОАЭ"),
        (Code("THB"), "Тайский бат"),
        (Code("VND"), "Вьетнамский донг"),
        (Code("INR"), "Индийская рупия"),
        (Code("JPY"), "Японская иена"),
        (Code("KRW"), "Южнокорейская вона"),
        (Code("EGP"), "Египетский фунт"),
        (Code("RSD"), "Сербский динар"),
        (Code("HUF"), "Венгерский форинт"),
        (Code("RON"), "Румынский лей"),
        (Code("BGN"), "Болгарский лев"),
        (Code("SEK"), "Шведская крона"),
        (Code("NOK"), "Норвежская крона"),
        (Code("DKK"), "Датская крона"),
        (Code("CAD"), "Канадский доллар"),
        (Code("AUD"), "Австралийский доллар"),
        (Code("HKD"), "Гонконгский доллар"),
        (Code("SGD"), "Сингапурский доллар"),
        (Code("BRL"), "Бразильский реал"),
        (Code("ZAR"), "Южноафриканский рэнд"),
        (Code("TMT"), "Туркменский манат"),
    ];

    /// <summary>«Белорусский рубль»; the code itself for an unknown one.</summary>
    public static string Of(CurrencyCode code) =>
        Known.FirstOrDefault(k => k.Code == code) is { Name: { } name } ? name : code.Value;

    private static CurrencyCode Code(string value) => CurrencyCode.Parse(value);
}
