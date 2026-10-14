using FinanceApp.Domain;

namespace FinanceApp.Infrastructure.Persistence;

/// <summary>Rubles per one unit of <see cref="Currency"/> as of <see cref="Date"/> (the date it was asked for).</summary>
public sealed class CachedExchangeRate
{
    public DateOnly Date { get; set; }

    public CurrencyCode Currency { get; set; }

    public decimal RubPerUnit { get; set; }

    /// <summary>Date the rate was actually published by the Central Bank (may be earlier on weekends).</summary>
    public DateOnly PublishedDate { get; set; }

    /// <summary>UTC.</summary>
    public DateTime FetchedAt { get; set; }
}
