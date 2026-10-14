using System.Diagnostics.CodeAnalysis;

namespace FinanceApp.Domain;

/// <summary>ISO 4217 alphabetic currency code, e.g. RUB, USD.</summary>
public readonly record struct CurrencyCode
{
    public static readonly CurrencyCode Rub = new("RUB");
    public static readonly CurrencyCode Usd = new("USD");
    public static readonly CurrencyCode Eur = new("EUR");
    public static readonly CurrencyCode Byn = new("BYN");

    private readonly string? _value;

    private CurrencyCode(string value) => _value = value;

    /// <summary>Upper-case three-letter code. <c>default(CurrencyCode)</c> reads as RUB.</summary>
    public string Value => _value ?? "RUB";

    public static CurrencyCode Parse(string value) =>
        TryParse(value, out var code)
            ? code
            : throw new DomainException($"Некорректный код валюты: «{value}». Нужен трёхбуквенный код ISO 4217, например RUB.");

    public static bool TryParse([NotNullWhen(true)] string? value, out CurrencyCode code)
    {
        code = default;
        if (value is null)
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.Length != 3 || !trimmed.All(char.IsAsciiLetter))
        {
            return false;
        }

        code = new CurrencyCode(trimmed.ToUpperInvariant());
        return true;
    }

    public bool Equals(CurrencyCode other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;
}
