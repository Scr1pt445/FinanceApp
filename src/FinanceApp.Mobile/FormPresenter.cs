using System.ComponentModel;
using FinanceApp.Mobile.Views;
using FinanceApp.Presentation.Finance;
using FinanceApp.Presentation.Home;

namespace FinanceApp.Mobile;

/// <summary>
/// The desktop shows the operation and account forms in place; the phone shows them as full screens over the
/// tabs. <see cref="ShellViewModel"/> opens and closes the forms; this pushes and pops the matching page.
/// </summary>
public sealed class FormPresenter(ShellViewModel shell, HomeViewModel home)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private INavigation? _navigation;
    private Page? _shown;

    public void Attach(INavigation navigation)
    {
        _navigation = navigation;
        shell.PropertyChanged += OnShellChanged;
    }

    private async void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(ShellViewModel.Form) or nameof(ShellViewModel.AccountForm)))
        {
            return;
        }

        try
        {
            await SyncAsync();
        }
#pragma warning disable CA1031 // An event handler: a form that cannot open must not take the app down. It is kept for the report.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            CrashReport.Save(ex);
        }
    }

    /// <summary>
    /// One change at a time: opening a form closes the other one first, so two notifications arrive back to
    /// back, and pushing and popping await. Each pass shows whatever the shell holds by then.
    /// </summary>
    private async Task SyncAsync()
    {
        if (_navigation is null)
        {
            return;
        }

        await _gate.WaitAsync();
        try
        {
            Page? wanted = shell.Form is { } form ? Reuse<TransactionFormPage>(p => p.Form == form) ?? new TransactionFormPage(form)
                : shell.AccountForm is { } account ? Reuse<AccountFormPage>(p => p.Form == account) ?? new AccountFormPage(account)
                : null;
            if (ReferenceEquals(wanted, _shown))
            {
                return;
            }

            if (_shown is not null && _navigation.ModalStack.Contains(_shown))
            {
                await _navigation.PopModalAsync();
            }

            _shown = wanted;
            if (wanted is not null)
            {
                await _navigation.PushModalAsync(wanted);
            }
            else
            {
                // A saved form changed the balances; the home screen keeps its own copy of them.
                await home.RefreshAsync();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private T? Reuse<T>(Func<T, bool> matches)
        where T : Page => _shown is T page && matches(page) ? page : null;
}
