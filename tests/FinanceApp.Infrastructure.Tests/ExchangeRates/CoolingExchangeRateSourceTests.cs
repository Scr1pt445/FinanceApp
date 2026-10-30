using System.Net;
using FinanceApp.Application.ExchangeRates;
using FinanceApp.Infrastructure.ExchangeRates;
using Microsoft.Extensions.Time.Testing;

namespace FinanceApp.Infrastructure.Tests.ExchangeRates;

public class CoolingExchangeRateSourceTests
{
    private static readonly DateOnly Day = new(2025, 10, 18);
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2025, 10, 18, 12, 0, 0, TimeSpan.Zero));
    private int _requests;
    private bool _online;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private CoolingExchangeRateSource Create(RateSourceCooldown cooldown) =>
        new(new CbrExchangeRateSource(new HttpClient(new Handler(this)) { BaseAddress = CbrExchangeRateSource.DefaultBaseAddress }), cooldown);

    [Fact]
    public async Task AfterAFailure_DoesNotAskTheNetworkAgain_ForAWhile()
    {
        var cooldown = new RateSourceCooldown(_time);

        await Should.ThrowAsync<ExchangeRateSourceException>(() => Create(cooldown).GetRubRatesAsync(Day, Ct));
        var afterFirst = _requests;
        _online = true;
        await Should.ThrowAsync<ExchangeRateSourceException>(() => Create(cooldown).GetRubRatesAsync(Day, Ct));

        _requests.ShouldBe(afterFirst);
        _time.Advance(RateSourceCooldown.Pause);
        (await Create(cooldown).GetRubRatesAsync(Day, Ct)).RubPerUnit.ShouldNotBeEmpty();
        cooldown.IsCoolingDown.ShouldBeFalse();
    }

    [Fact]
    public async Task AnyNetworkException_IsNoRatesNow()
    {
        var source = new CbrExchangeRateSource(new HttpClient(new Throwing()) { BaseAddress = CbrExchangeRateSource.DefaultBaseAddress });

        // Android reports a lost network in its own ways, not always as HttpRequestException.
        await Should.ThrowAsync<ExchangeRateSourceException>(() => source.GetRubRatesAsync(Day, Ct));
    }

    private sealed class Handler(CoolingExchangeRateSourceTests test) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            test._requests++;
            if (!test._online)
            {
                throw new HttpRequestException("offline");
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{ "Date": "2025-10-18T11:30:00+03:00", "Valute": { "USD": { "CharCode": "USD", "Nominal": 1, "Value": 81.05 } } }"""),
            });
        }
    }

    private sealed class Throwing : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new IOException("Network is unreachable");
    }
}
