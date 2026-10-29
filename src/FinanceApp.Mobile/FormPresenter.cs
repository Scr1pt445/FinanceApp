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
    private INavigation? _navigation;
    private Page? _shown;

    public void Attach(INavigation navigation)
    {
        _navigation = navigation;
        shell.PropertyChanged += OnShellChanged;
    }

    private async void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(ShellViewModel.Form) or nameof(ShellViewModel.AccountForm)) || _navigation is null)
        {
            return;
        }

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

    private T? Reuse<T>(Func<T, bool> matches)
        where T : Page => _shown is T page && matches(page) ? page : null;
}
