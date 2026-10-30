namespace FinanceApp.Mobile.Views;

/// <summary>Shown for the moment the database is being prepared, or with the reason it could not be.</summary>
public sealed class StartupPage : ContentPage
{
    private readonly Label _message = new()
    {
        Text = "FinanceApp",
        FontSize = 22,
        FontAttributes = FontAttributes.Bold,
        HorizontalTextAlignment = TextAlignment.Center,
    };

    private readonly ActivityIndicator _spinner = new() { IsRunning = true };

    public StartupPage()
    {
        Content = new VerticalStackLayout
        {
            Spacing = 16,
            Padding = new Thickness(32),
            VerticalOptions = LayoutOptions.Center,
            Children = { _message, _spinner },
        };
    }

    public void ShowError(string message)
    {
        _spinner.IsRunning = false;
        _spinner.IsVisible = false;
        _message.FontSize = 15;
        _message.FontAttributes = FontAttributes.None;
        _message.TextColor = Palette.Warning;
        _message.Text = message;
    }
}
