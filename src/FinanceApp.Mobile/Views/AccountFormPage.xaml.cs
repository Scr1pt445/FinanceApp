using FinanceApp.Presentation.Finance;

namespace FinanceApp.Mobile.Views;

/// <summary>Shown over the tabs while <see cref="ShellViewModel.AccountForm"/> is open (see <see cref="FormPresenter"/>).</summary>
public partial class AccountFormPage : ContentPage
{
    public AccountFormPage(AccountFormViewModel form)
    {
        InitializeComponent();
        BindingContext = Form = form;
    }

    public AccountFormViewModel Form { get; }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (Form.IsNew)
        {
            NameField.Focus();
        }
    }

    protected override bool OnBackButtonPressed()
    {
        Form.CancelCommand.Execute(null);
        return true;
    }
}
