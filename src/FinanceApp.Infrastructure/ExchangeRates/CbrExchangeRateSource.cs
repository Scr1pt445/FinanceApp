using System.Globalization;
using System.Net;
using System.Text.Json;
using FinanceApp.Application.ExchangeRates;
using FinanceApp.Domain;

namespace FinanceApp.Infrastructure.ExchangeRates;

/// <summary>
/// Official Central Bank of Russia rates via the cbr-xml-daily.ru mirror (JSON archive by date).
/// The CBR does not publish on weekends and holidays — the archive answers 404 for those days,
/// so we step back to the last published day.
/// </summary>
public sealed class CbrExchangeRateSource(HttpClient httpClient) : IExchangeRateSource
{
    public static readonly Uri DefaultBaseAddress = new("https://www.cbr-xml-daily.ru/");

    /// <summary>Longest gap without publication (New Year holidays) plus a margin.</summary>
    public const int MaxDaysBack = 14;

    public async Task<RubRates> GetRubRatesAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        for (var day = date; day > date.AddDays(-MaxDaysBack); day = day.AddDays(-1))
        {
            var url = string.Create(CultureInfo.InvariantCulture, $"archive/{day:yyyy}/{day:MM}/{day:dd}/daily_json.js");
            HttpResponseMessage response;
            try
            {
                response = await httpClient.GetAsync(new Uri(url, UriKind.Relative), cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                throw new ExchangeRateSourceException("Сервис курсов ЦБ недоступен.", ex);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new ExchangeRateSourceException("Сервис курсов ЦБ не ответил вовремя.", ex);
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new ExchangeRateSourceException($"Сервис курсов ЦБ ответил {(int)response.StatusCode}.");
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                return await ParseAsync(stream, day, cancellationToken);
            }
        }

        throw new ExchangeRateSourceException($"ЦБ не публиковал курсы в течение {MaxDaysBack} дней до {date:yyyy-MM-dd}.");
    }

    internal static async Task<RubRates> ParseAsync(Stream json, DateOnly requestedDay, CancellationToken cancellationToken)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(json, cancellationToken: cancellationToken);
            var root = document.RootElement;

            var published = root.TryGetProperty("Date", out var dateElement) && dateElement.TryGetDateTimeOffset(out var dateTime)
                ? DateOnly.FromDateTime(dateTime.DateTime)
                : requestedDay;

            var rates = new Dictionary<CurrencyCode, decimal>();
            foreach (var valute in root.GetProperty("Valute").EnumerateObject())
            {
                var charCode = valute.Value.GetProperty("CharCode").GetString();
                var nominal = valute.Value.GetProperty("Nominal").GetDecimal();
                var value = valute.Value.GetProperty("Value").GetDecimal();
                if (CurrencyCode.TryParse(charCode, out var code) && nominal > 0 && value > 0)
                {
                    rates[code] = value / nominal;
                }
            }

            return new RubRates(published, rates);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new ExchangeRateSourceException("Сервис курсов ЦБ вернул данные в неожиданном формате.", ex);
        }
    }
}
