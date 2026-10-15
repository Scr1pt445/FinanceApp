using FinanceApp.Application.Settings;
using FinanceApp.Presentation.Settings;

namespace FinanceApp.Core.Tests.Presentation;

public class HotKeyGestureTests
{
    [Theory]
    [InlineData("Ctrl+Alt+M", HotKeyModifiers.Ctrl | HotKeyModifiers.Alt, "M")]
    [InlineData("alt + ctrl + m", HotKeyModifiers.Ctrl | HotKeyModifiers.Alt, "M")]
    [InlineData("Win+Shift+F12", HotKeyModifiers.Win | HotKeyModifiers.Shift, "F12")]
    [InlineData("Control+Space", HotKeyModifiers.Ctrl, "Space")]
    [InlineData("Ctrl+Shift+5", HotKeyModifiers.Ctrl | HotKeyModifiers.Shift, "5")]
    public void Parse_Valid(string text, HotKeyModifiers modifiers, string key)
    {
        HotKeyGesture.TryParse(text, out var gesture).ShouldBeTrue();

        gesture.ShouldBe(new HotKeyGesture(modifiers, key));
    }

    [Theory]
    [InlineData("")]
    [InlineData("M")]
    [InlineData("Shift+M")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+M+N")]
    [InlineData("Ctrl+Enter")]
    [InlineData("Ctrl+F25")]
    [InlineData("Ctrl+Ж")]
    public void Parse_Invalid(string text) => HotKeyGesture.TryParse(text, out _).ShouldBeFalse();

    [Fact]
    public void ToString_UsesCanonicalModifierOrder() =>
        new HotKeyGesture(HotKeyModifiers.Win | HotKeyModifiers.Shift | HotKeyModifiers.Alt | HotKeyModifiers.Ctrl, "Q")
            .ToString().ShouldBe("Ctrl+Alt+Shift+Win+Q");

    [Fact]
    public void Default_IsValid() =>
        HotKeyGesture.TryParse(AppSettings.DefaultQuickEntryHotKey, out _).ShouldBeTrue();
}

public class HotKeySettingsViewModelTests
{
    private readonly FakeRegistrar _registrar = new();
    private readonly FakeSettings _settings = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HotKeySettingsViewModel Create(string? startupError = null)
    {
        var viewModel = new HotKeySettingsViewModel(_registrar, _settings);
        viewModel.Load(startupError);
        return viewModel;
    }

    [Fact]
    public void Load_ShowsCurrent()
    {
        _registrar.Current = new HotKeyGesture(HotKeyModifiers.Ctrl | HotKeyModifiers.Alt, "F");

        var viewModel = Create();

        viewModel.Current.ShouldBe("Ctrl+Alt+F");
        viewModel.Message.ShouldBe(HotKeySettingsViewModel.PressPrompt);
        viewModel.SaveCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public void Load_WithStartupError_ExplainsWhy()
    {
        var viewModel = Create("Ctrl+Alt+M занято другим приложением.");

        viewModel.Current.ShouldBe("не назначена");
        viewModel.HasProblem.ShouldBeTrue();
        viewModel.Message.ShouldContain("занято");
    }

    [Fact]
    public void Record_ModifiersOnly_ShowsWhatIsHeld()
    {
        var viewModel = Create();

        viewModel.Record(HotKeyModifiers.Ctrl | HotKeyModifiers.Alt, null);

        viewModel.Recorded.ShouldBe("Ctrl+Alt+…");
        viewModel.SaveCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public void Record_WithoutCtrlAltWin_IsRejected()
    {
        var viewModel = Create();

        viewModel.Record(HotKeyModifiers.Shift, "M");

        viewModel.HasProblem.ShouldBeTrue();
        viewModel.SaveCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public async Task Save_RegistersThenPersists()
    {
        var viewModel = Create();
        HotKeyGesture? saved = null;
        viewModel.Saved += (_, g) => saved = g;
        viewModel.Record(HotKeyModifiers.Ctrl | HotKeyModifiers.Shift, "Q");

        await viewModel.SaveCommand.ExecuteAsync(null);

        _registrar.Current.ShouldBe(new HotKeyGesture(HotKeyModifiers.Ctrl | HotKeyModifiers.Shift, "Q"));
        _settings.Value.QuickEntryHotKey.ShouldBe("Ctrl+Shift+Q");
        viewModel.Current.ShouldBe("Ctrl+Shift+Q");
        saved.ShouldNotBeNull();
    }

    [Fact]
    public async Task Save_TakenCombination_KeepsOldOne_AndSettings()
    {
        _registrar.Current = new HotKeyGesture(HotKeyModifiers.Ctrl | HotKeyModifiers.Alt, "F");
        _registrar.Taken.Add("Ctrl+Alt+N");
        var viewModel = Create();
        viewModel.Record(HotKeyModifiers.Ctrl | HotKeyModifiers.Alt, "N");

        await viewModel.SaveCommand.ExecuteAsync(null);

        _registrar.Current.ToString().ShouldBe("Ctrl+Alt+F");
        _settings.Value.QuickEntryHotKey.ShouldBe(AppSettings.DefaultQuickEntryHotKey);
        viewModel.HasProblem.ShouldBeTrue();
        viewModel.Message.ShouldContain("Ctrl+Alt+N");
    }

    [Fact]
    public void UseDefault_ProposesCtrlAltM()
    {
        var viewModel = Create();

        viewModel.UseDefaultCommand.Execute(null);

        viewModel.Recorded.ShouldBe("Ctrl+Alt+M");
        viewModel.SaveCommand.CanExecute(null).ShouldBeTrue();
    }

    private sealed class FakeRegistrar : IHotKeyRegistrar
    {
        public HotKeyGesture? Current { get; set; }

        public HashSet<string> Taken { get; } = [];

        public bool TryRegister(HotKeyGesture gesture, out string? error)
        {
            if (Taken.Contains(gesture.ToString()))
            {
                error = $"{gesture} занято другим приложением.";
                return false;
            }

            Current = gesture;
            error = null;
            return true;
        }
    }

    private sealed class FakeSettings : ISettingsStore
    {
        public AppSettings Value { get; private set; } = new();

        public AppSettings Load() => Value;

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            Value = settings;
            return Task.CompletedTask;
        }
    }
}
