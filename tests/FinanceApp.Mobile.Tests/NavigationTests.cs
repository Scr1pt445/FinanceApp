using FinanceApp.Mobile.Views;
using FinanceApp.Presentation.Finance;

namespace FinanceApp.Mobile.Tests;

/// <summary>How the app starts and how forms come and go over the tabs.</summary>
public sealed class NavigationTests : IAsyncLifetime
{
    private readonly FakeNavigation _navigation = new();
    private MobileApp _app = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _app = await MobileApp.StartAsync();

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public async Task Start_ShowsTheTabsOnceTheDatabaseIsReady()
    {
        var window = ((IApplication)_app.App).CreateWindow(null!);

        await WaitUntil(() => window is Window { Page: AppShell }, () => (window as Window)?.Page is StartupPage { Content: Layout layout }
            ? string.Join(" | ", layout.Children.OfType<Label>().Select(l => l.Text))
            : (window as Window)?.Page?.GetType().Name ?? "no page");
    }

    [Fact]
    public async Task Form_IsShownOverTheTabs_AndGoesAwayOnCancel()
    {
        var shell = _app.Get<ShellViewModel>();
        _app.Get<FormPresenter>().Attach(_navigation);

        await shell.NewExpenseCommand.ExecuteAsync(null);
        await WaitUntil(() => _navigation.ModalStack.Count == 1);
        var page = _navigation.ModalStack[0].ShouldBeOfType<TransactionFormPage>();
        page.Form.ShouldBeSameAs(shell.Form);

        page.Form.CancelCommand.Execute(null);
        await WaitUntil(() => _navigation.ModalStack.Count == 0);
    }

    [Fact]
    public async Task SavedForm_GoesAway_AndTheHomeScreenSeesTheOperation()
    {
        var shell = _app.Get<ShellViewModel>();
        var home = _app.Get<Presentation.Home.HomeViewModel>();
        _app.Get<FormPresenter>().Attach(_navigation);

        await shell.NewExpenseCommand.ExecuteAsync(null);
        await WaitUntil(() => _navigation.ModalStack.Count == 1);
        var form = shell.Form!;
        form.AmountText = "1200";
        form.ChooseCategoryCommand.Execute(form.Tiles[0]);
        await form.SaveCommand.ExecuteAsync(null);

        form.SaveError.ShouldBeEmpty();
        await WaitUntil(() => _navigation.ModalStack.Count == 0);
        await WaitUntil(() => home.Recent.Count == 1);
    }

    [Fact]
    public async Task AccountForm_ReplacesAnOpenOperationForm()
    {
        var shell = _app.Get<ShellViewModel>();
        _app.Get<FormPresenter>().Attach(_navigation);

        await shell.NewExpenseCommand.ExecuteAsync(null);
        await WaitUntil(() => _navigation.ModalStack.Count == 1);
        await shell.NewEnvelopeCommand.ExecuteAsync(null);

        await WaitUntil(() => _navigation.ModalStack is [AccountFormPage]);
    }

    private static async Task WaitUntil(Func<bool> condition, Func<string>? state = null)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(20, Ct);
        }

        condition().ShouldBeTrue(state?.Invoke());
    }

    /// <summary>The modal stack of the phone's window, without the window.</summary>
    private sealed class FakeNavigation : INavigation
    {
        private readonly List<Page> _modal = [];

        public IReadOnlyList<Page> ModalStack => _modal;

        public IReadOnlyList<Page> NavigationStack => [];

        public Task PushModalAsync(Page page)
        {
            _modal.Add(page);
            return Task.CompletedTask;
        }

        public Task<Page> PopModalAsync()
        {
            var page = _modal[^1];
            _modal.RemoveAt(_modal.Count - 1);
            return Task.FromResult(page);
        }

        public Task PushModalAsync(Page page, bool animated) => PushModalAsync(page);

        public Task<Page> PopModalAsync(bool animated) => PopModalAsync();

        public void InsertPageBefore(Page page, Page before) => throw new NotSupportedException();

        public Task<Page> PopAsync() => throw new NotSupportedException();

        public Task<Page> PopAsync(bool animated) => throw new NotSupportedException();

        public Task PopToRootAsync() => throw new NotSupportedException();

        public Task PopToRootAsync(bool animated) => throw new NotSupportedException();

        public Task PushAsync(Page page) => throw new NotSupportedException();

        public Task PushAsync(Page page, bool animated) => throw new NotSupportedException();

        public void RemovePage(Page page) => throw new NotSupportedException();
    }
}
