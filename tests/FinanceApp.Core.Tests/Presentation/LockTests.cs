using FinanceApp.Application.Settings;
using FinanceApp.Presentation.Settings;
using Microsoft.Extensions.Time.Testing;

namespace FinanceApp.Core.Tests.Presentation;

/// <summary>The PIN lock: only a salted hash is kept; five wrong tries make the keypad wait.</summary>
public class LockTests
{
    private readonly MemorySettings _settings = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero));

    private static void Type(PinPadViewModel pad, string pin)
    {
        foreach (var digit in pin)
        {
            pad.PressCommand.Execute(digit.ToString());
        }
    }

    [Fact]
    public void Pin_IsKeptAsASaltedHash()
    {
        var a = PinCode.Create("1234", biometrics: false);
        var b = PinCode.Create("1234", biometrics: false);

        a.PinHash.ShouldNotBeNull().ShouldNotContain("1234");
        a.PinHash.ShouldNotBe(b.PinHash); // different salts
        PinCode.Verify(a, "1234").ShouldBeTrue();
        PinCode.Verify(a, "4321").ShouldBeFalse();
        PinCode.Verify(new LockSettings(), "1234").ShouldBeFalse();
        PinCode.IsValid("12a4").ShouldBeFalse();
    }

    [Fact]
    public async Task Setup_AsksTwice_AndSavesOnlyAMatch()
    {
        var setup = new PinSetupViewModel(_settings);
        bool? finished = null;
        setup.Finished += (_, saved) => finished = saved;

        Type(setup, "1234");
        setup.Title.ShouldBe("Повторите PIN");
        Type(setup, "1235");
        setup.IsWrong.ShouldBeTrue();
        setup.Title.ShouldBe("Придумайте PIN");
        _settings.Load().Lock.IsOn.ShouldBeFalse();

        Type(setup, "2580");
        Type(setup, "2580");
        await Task.Yield();

        finished.ShouldBe(true);
        PinCode.Verify(_settings.Load().Lock, "2580").ShouldBeTrue();
    }

    [Fact]
    public async Task Lock_OpensWithTheRightPin()
    {
        await _settings.SaveAsync(new AppSettings { Lock = PinCode.Create("2580", biometrics: true) }, TestContext.Current.CancellationToken);
        var lockScreen = new LockViewModel(_settings, _time);
        var unlocked = 0;
        lockScreen.Unlocked += (_, _) => unlocked++;
        lockScreen.Reset();
        lockScreen.Message.ShouldBe("Введите PIN или приложите палец");

        Type(lockScreen, "1111");
        unlocked.ShouldBe(0);
        lockScreen.Message.ShouldBe("Неверный PIN. Осталось попыток: 4.");
        lockScreen.Dots.ShouldAllBe(d => !d);

        Type(lockScreen, "2580");
        unlocked.ShouldBe(1);
    }

    [Fact]
    public async Task FiveWrongPins_MakeTheKeypadWait()
    {
        await _settings.SaveAsync(new AppSettings { Lock = PinCode.Create("2580", biometrics: false) }, TestContext.Current.CancellationToken);
        var lockScreen = new LockViewModel(_settings, _time);
        var unlocked = 0;
        lockScreen.Unlocked += (_, _) => unlocked++;
        for (var i = 0; i < LockViewModel.FreeAttempts; i++)
        {
            Type(lockScreen, "0000");
        }

        Type(lockScreen, "2580");
        unlocked.ShouldBe(0);
        lockScreen.Message.ShouldStartWith("Слишком много попыток");

        _time.Advance(TimeSpan.FromSeconds(31));
        Type(lockScreen, "2580");
        unlocked.ShouldBe(1);
    }

    [Fact]
    public async Task WrongPins_AreRemembered_WhenTheAppIsClosed()
    {
        await _settings.SaveAsync(new AppSettings { Lock = PinCode.Create("2580", biometrics: false) }, TestContext.Current.CancellationToken);
        var first = new LockViewModel(_settings, _time);
        for (var i = 0; i < LockViewModel.FreeAttempts; i++)
        {
            Type(first, "0000");
        }

        _settings.Load().Lock.FailedAttempts.ShouldBe(LockViewModel.FreeAttempts);

        // The app is closed and opened again: a new lock screen, the same settings.
        var again = new LockViewModel(_settings, _time);
        var unlocked = 0;
        again.Unlocked += (_, _) => unlocked++;
        Type(again, "2580");
        unlocked.ShouldBe(0);
        again.Message.ShouldStartWith("Слишком много попыток");

        _time.Advance(TimeSpan.FromSeconds(31));
        Type(again, "0000");
        again.Message.ShouldBe("Неверный PIN. Следующая попытка через 60 с.");

        _time.Advance(TimeSpan.FromSeconds(61));
        Type(again, "2580");
        unlocked.ShouldBe(1);
        _settings.Load().Lock.FailedAttempts.ShouldBe(0);
        _settings.Load().Lock.RetryAfter.ShouldBeNull();
    }

    [Fact]
    public async Task ClockTurnedBack_DoesNotLockForever()
    {
        var lockSettings = PinCode.Create("2580", biometrics: false) with
        {
            FailedAttempts = 9,
            RetryAfter = _time.GetUtcNow().AddDays(30),
        };
        await _settings.SaveAsync(new AppSettings { Lock = lockSettings }, TestContext.Current.CancellationToken);
        var lockScreen = new LockViewModel(_settings, _time);
        var unlocked = 0;
        lockScreen.Unlocked += (_, _) => unlocked++;

        Type(lockScreen, "2580");
        unlocked.ShouldBe(0);
        lockScreen.Message.ShouldBe($"Слишком много попыток. Подождите {LockViewModel.LongestWait.TotalSeconds} с.");

        _time.Advance(LockViewModel.LongestWait + TimeSpan.FromSeconds(1));
        Type(lockScreen, "2580");
        unlocked.ShouldBe(1);
    }

    [Fact]
    public async Task ANewPin_StartsTheCountAfresh()
    {
        await _settings.SaveAsync(new AppSettings { Lock = PinCode.Create("2580", biometrics: false) }, TestContext.Current.CancellationToken);
        var lockScreen = new LockViewModel(_settings, _time);
        Type(lockScreen, "0000");
        Type(lockScreen, "0000");

        await _settings.SaveAsync(new AppSettings { Lock = PinCode.Create("1357", biometrics: false) }, TestContext.Current.CancellationToken);
        Type(lockScreen, "0000");

        lockScreen.Message.ShouldBe("Неверный PIN. Осталось попыток: 4.");
    }

    [Fact]
    public async Task Fingerprint_Unlocks_WhenAllowed()
    {
        await _settings.SaveAsync(new AppSettings { Lock = PinCode.Create("2580", biometrics: true) }, TestContext.Current.CancellationToken);
        var lockScreen = new LockViewModel(_settings, _time);
        var unlocked = 0;
        lockScreen.Unlocked += (_, _) => unlocked++;

        lockScreen.BiometricsOn.ShouldBeTrue();
        lockScreen.UnlockByBiometrics();

        unlocked.ShouldBe(1);
    }

    private sealed class MemorySettings : ISettingsStore
    {
        private AppSettings _current = new();

        public AppSettings Load() => _current;

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            _current = settings;
            return Task.CompletedTask;
        }
    }
}
