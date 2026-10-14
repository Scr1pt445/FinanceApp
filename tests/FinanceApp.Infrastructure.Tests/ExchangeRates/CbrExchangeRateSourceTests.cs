using System.Net;
using System.Text;
using FinanceApp.Application.ExchangeRates;
using FinanceApp.Domain;
using FinanceApp.Infrastructure.ExchangeRates;

namespace FinanceApp.Infrastructure.Tests.ExchangeRates;

public class CbrExchangeRateSourceTests
{
    // Shape of https://www.cbr-xml-daily.ru/archive/2025/10/18/daily_json.js (values shortened).
    private const string DailyJson = """
        {
            "Date": "2025-10-18T11:30:00+03:00",
            "PreviousDate": "2025-10-17T11:30:00+03:00",
            "Timestamp": "2025-10-17T20:00:00+03:00",
            "Valute": {
                "USD": { "ID": "R01235", "NumCode": "840", "CharCode": "USD", "Nominal": 1, "Name": "Доллар США", "Value": 81.0548, "Previous": 80.9 },
                "EUR": { "ID": "R01239", "NumCode": "978", "CharCode": "EUR", "Nominal": 1, "Name": "Евро", "Value": 94.7521, "Previous": 94.1 },
                "BYN": { "ID": "R01090B", "NumCode": "933", "CharCode": "BYN", "Nominal": 1, "Name": "Белорусский рубль", "Value": 27.2185, "Previous": 27.1 },
                "AMD": { "ID": "R01060", "NumCode": "051", "CharCode": "AMD", "Nominal": 100, "Name": "Армянских драмов", "Value": 21.1528, "Previous": 21.0 }
            }
        }
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static CbrExchangeRateSource CreateSource(FakeHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = CbrExchangeRateSource.DefaultBaseAddress });

    [Fact]
    public async Task Parses_RatesAndDividesByNominal()
    {
        var handler = new FakeHandler(_ => Json(DailyJson));

        var rates = await CreateSource(handler).GetRubRatesAsync(new DateOnly(2025, 10, 18), Ct);

        rates.PublishedDate.ShouldBe(new DateOnly(2025, 10, 18));
        rates.RubPerUnit[CurrencyCode.Usd].ShouldBe(81.0548m);
        rates.RubPerUnit[CurrencyCode.Byn].ShouldBe(27.2185m);
        rates.RubPerUnit[CurrencyCode.Parse("AMD")].ShouldBe(0.211528m);
        handler.Requests.ShouldBe(["https://www.cbr-xml-daily.ru/archive/2025/10/18/daily_json.js"]);
    }

    [Fact]
    public async Task Weekend_StepsBackToLastPublishedDay()
    {
        var handler = new FakeHandler(uri => uri.EndsWith("2025/10/18/daily_json.js", StringComparison.Ordinal)
            ? Json(DailyJson)
            : new HttpResponseMessage(HttpStatusCode.NotFound));

        var rates = await CreateSource(handler).GetRubRatesAsync(new DateOnly(2025, 10, 20), Ct);

        rates.RubPerUnit[CurrencyCode.Usd].ShouldBe(81.0548m);
        handler.Requests.Count.ShouldBe(3);
    }

    [Fact]
    public async Task NetworkError_IsSourceException()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("no network"));

        await Should.ThrowAsync<ExchangeRateSourceException>(
            () => CreateSource(handler).GetRubRatesAsync(new DateOnly(2025, 10, 18), Ct));
    }

    [Fact]
    public async Task ServerError_IsSourceException()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        await Should.ThrowAsync<ExchangeRateSourceException>(
            () => CreateSource(handler).GetRubRatesAsync(new DateOnly(2025, 10, 18), Ct));
    }

    [Fact]
    public async Task UnexpectedJson_IsSourceException()
    {
        var handler = new FakeHandler(_ => Json("""{ "Date": "2025-10-18T11:30:00+03:00" }"""));

        await Should.ThrowAsync<ExchangeRateSourceException>(
            () => CreateSource(handler).GetRubRatesAsync(new DateOnly(2025, 10, 18), Ct));
    }

    [Fact]
    public async Task NothingPublishedForTwoWeeks_IsSourceException()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        await Should.ThrowAsync<ExchangeRateSourceException>(
            () => CreateSource(handler).GetRubRatesAsync(new DateOnly(2025, 10, 18), Ct));
        handler.Requests.Count.ShouldBe(CbrExchangeRateSource.MaxDaysBack);
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/javascript") };

    private sealed class FakeHandler(Func<string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!.ToString();
            Requests.Add(uri);
            return Task.FromResult(respond(uri));
        }
    }
}
