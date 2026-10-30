using FinanceApp.Application.Settings;
using FinanceApp.Presentation.Home;
using FinanceApp.Presentation.QuickEntry;
using FinanceApp.Presentation.Settings;
using Microsoft.Extensions.Time.Testing;

namespace FinanceApp.Core.Tests.Presentation;

public class PhoneSettingsViewModelTests
{
    private readonly MemorySettings _settings = new();
    private readonly HomeViewModel _home = new(new FakeFinanceBackend(), new QuickEntryViewModel(new NoQuickEntry()), new FakeTimeProvider());

    [Fact]
    public void QuickEntryLine_IsOffByDefault()
    {
        var settings = new PhoneSettingsViewModel(_settings, _home);

        settings.Load();

        settings.QuickEntryLine.ShouldBeFalse();
        _home.QuickEntryOn.ShouldBeFalse();
        _settings.Saves.ShouldBe(0); // loading changes nothing
    }

    [Fact]
    public void TurningItOn_ShowsTheLine_AndIsRemembered()
    {
        var settings = new PhoneSettingsViewModel(_settings, _home);
        settings.Load();

        settings.QuickEntryLine = true;

        _home.QuickEntryOn.ShouldBeTrue();
        _settings.Load().QuickEntryLine.ShouldBeTrue();
        new PhoneSettingsViewModel(_settings, _home).ShouldSatisfyAllConditions(s => s.Load(), s => s.QuickEntryLine.ShouldBeTrue());
    }

    [Fact]
    public void BaseCurrency_IsNamed() =>
        new PhoneSettingsViewModel(_settings, _home).BaseCurrencyText.ShouldStartWith("Основная валюта — RUB.");

    private sealed class MemorySettings : ISettingsStore
    {
        private AppSettings _current = new();

        public int Saves { get; private set; }

        public AppSettings Load() => _current;

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            _current = settings;
            Saves++;
            return Task.CompletedTask;
        }
    }

    private sealed class NoQuickEntry : IQuickEntryBackend
    {
        public Task<FinanceApp.Application.QuickEntry.QuickEntryContext> LoadContextAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<FinanceApp.Application.Transactions.SavedTransaction> SaveAsync(string line, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
