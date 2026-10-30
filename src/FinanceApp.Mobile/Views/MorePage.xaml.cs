using System.Reflection;
using FinanceApp.Presentation.Data;
using FinanceApp.Presentation.Finance;
using FinanceApp.Presentation.Settings;
using FinanceApp.Presentation.Setup;

namespace FinanceApp.Mobile.Views;

public partial class MorePage : ContentPage
{
    private readonly DataViewModel _viewModel;
    private readonly Func<CategoriesPage> _categories;
    private readonly Func<RatesPage> _rates;
    private readonly Func<RecurringPage> _recurring;
    private readonly Func<DebtsPage> _debts;
    private readonly Func<CompareMonthsPage> _compare;
    private readonly DebtsViewModel _debtsViewModel;
    private readonly Func<CurrenciesPage> _currencies;
    private readonly CurrenciesViewModel _currenciesViewModel;
    private readonly PhoneSettingsViewModel _settings;
    private readonly IServiceProvider _services;

    public MorePage(
        DataViewModel viewModel,
        PhoneSettingsViewModel settings,
        Func<CategoriesPage> categories,
        Func<RatesPage> rates,
        Func<RecurringPage> recurring,
        Func<DebtsPage> debts,
        Func<CompareMonthsPage> compare,
        DebtsViewModel debtsViewModel,
        Func<CurrenciesPage> currencies,
        CurrenciesViewModel currenciesViewModel,
        IBiometrics biometrics,
        IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(biometrics);
        _rates = rates;
        _recurring = recurring;
        _debts = debts;
        _compare = compare;
        _debtsViewModel = debtsViewModel;
        _currencies = currencies;
        _currenciesViewModel = currenciesViewModel;
        _settings = settings;
        _services = services;
        settings.BiometricsAvailable = biometrics.IsAvailable;
        settings.WidgetChanged += (_, _) => PublishWidget();
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        SettingsCard.BindingContext = settings;
        DebtsRow.BindingContext = debtsViewModel;
        CurrenciesRow.BindingContext = currenciesViewModel;
        _categories = categories;
        var version = typeof(MorePage).Assembly.GetName().Version;
        VersionLabel.Text = version is null ? "FinanceApp" : $"FinanceApp {version.ToString(3)}";
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.Load();
        await _debtsViewModel.LoadAsync();
        await _currenciesViewModel.LoadAsync();
    }

    private async void OnCategories(object? sender, TappedEventArgs e) => await Navigation.PushAsync(_categories());

    private async void OnRates(object? sender, TappedEventArgs e) => await Navigation.PushAsync(_rates());

    private async void OnCurrencies(object? sender, TappedEventArgs e) => await Navigation.PushAsync(_currencies());

    private async void OnDebts(object? sender, TappedEventArgs e) => await Navigation.PushAsync(_debts());

    private async void OnCompare(object? sender, TappedEventArgs e) => await Navigation.PushAsync(_compare());

    private async void OnRecurring(object? sender, TappedEventArgs e) => await Navigation.PushAsync(_recurring());

    /// <summary>On: the PIN is set on its own screen (the switch goes back off if it is closed). Off: the PIN is removed.</summary>
    private async void OnLockToggled(object? sender, ToggledEventArgs e)
    {
        if (e.Value == _settings.LockOn)
        {
            return;
        }

        if (!e.Value)
        {
            await _settings.TurnOffLockAsync();
            PublishWidget();
            return;
        }

        var setup = _services.GetRequiredService<PinSetupViewModel>();
        setup.Biometrics = _settings.BiometricsAvailable;
        var page = new PinSetupPage(setup);
        setup.Finished += async (_, saved) =>
        {
            await Navigation.PopModalAsync();
            _settings.Reload();
            LockSwitch.IsToggled = _settings.LockOn;
            PublishWidget();
        };
        await Navigation.PushModalAsync(page);
    }

    private static void PublishWidget() => (Microsoft.Maui.Controls.Application.Current as App)?.PublishWidget();

    /// <summary>The first run's tour again, without the setting up; it closes itself at the end.</summary>
    private async void OnTour(object? sender, TappedEventArgs e)
    {
        var tour = SetupViewModel.ForTour();
        tour.Completed += async (_, _) => await Navigation.PopModalAsync();
        await Navigation.PushModalAsync(new SetupPage(tour));
    }
}
