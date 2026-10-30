using System.Reflection;
using FinanceApp.Presentation.Data;
using FinanceApp.Presentation.Settings;
using FinanceApp.Presentation.Setup;

namespace FinanceApp.Mobile.Views;

public partial class MorePage : ContentPage
{
    private readonly DataViewModel _viewModel;
    private readonly Func<CategoriesPage> _categories;
    private readonly Func<RatesPage> _rates;

    public MorePage(DataViewModel viewModel, PhoneSettingsViewModel settings, Func<CategoriesPage> categories, Func<RatesPage> rates)
    {
        _rates = rates;
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        SettingsCard.BindingContext = settings;
        _categories = categories;
        var version = typeof(MorePage).Assembly.GetName().Version;
        VersionLabel.Text = version is null ? "FinanceApp" : $"FinanceApp {version.ToString(3)}";
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.Load();
    }

    private async void OnCategories(object? sender, TappedEventArgs e) => await Navigation.PushAsync(_categories());

    private async void OnRates(object? sender, TappedEventArgs e) => await Navigation.PushAsync(_rates());

    /// <summary>The first run's tour again, without the setting up; it closes itself at the end.</summary>
    private async void OnTour(object? sender, TappedEventArgs e)
    {
        var tour = SetupViewModel.ForTour();
        tour.Completed += async (_, _) => await Navigation.PopModalAsync();
        await Navigation.PushModalAsync(new SetupPage(tour));
    }
}
