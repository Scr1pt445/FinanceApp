using FinanceApp.Mobile.Controls;
using FinanceApp.Presentation.Settings;

namespace FinanceApp.Mobile.Views;

/// <summary>«Придумайте PIN» → «Повторите PIN»: a modal page from «Ещё → Настройки».</summary>
public sealed class PinSetupPage : ContentPage
{
    private readonly PinSetupViewModel _setup;

    public PinSetupPage(PinSetupViewModel setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        _setup = setup;
        BackgroundColor = Palette.Window;
        var title = new Label { FontSize = 24, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center };
        title.SetBinding(Label.TextProperty, new Binding(nameof(PinSetupViewModel.Title), source: setup));
        var cancel = new Button { Text = "Отмена", Style = (Style)Microsoft.Maui.Controls.Application.Current!.Resources["Link"], HorizontalOptions = LayoutOptions.Center };
        cancel.Command = setup.CancelCommand;
        Content = new Grid
        {
            RowDefinitions = { new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star) },
            Padding = new Thickness(24),
            Children = { },
        };
        var grid = (Grid)Content;
        var header = new VerticalStackLayout
        {
            Spacing = 8,
            VerticalOptions = LayoutOptions.End,
            Margin = new Thickness(0, 0, 0, 28),
            Children =
            {
                title,
                new Label
                {
                    Text = "Четыре цифры. Приложение спросит их при каждом открытии.",
                    TextColor = Palette.MutedText,
                    FontSize = 14,
                    HorizontalTextAlignment = TextAlignment.Center,
                },
            },
        };
        grid.Add(header, 0, 0);
        grid.Add(new PinPadView { BindingContext = setup }, 0, 1);
        grid.Add(cancel, 0, 2);
    }

    public PinSetupViewModel Setup => _setup;

    protected override bool OnBackButtonPressed()
    {
        _setup.CancelCommand.Execute(null);
        return true;
    }
}
