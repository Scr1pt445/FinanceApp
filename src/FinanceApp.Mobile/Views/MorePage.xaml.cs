using System.Reflection;
using FinanceApp.Presentation.Data;

namespace FinanceApp.Mobile.Views;

public partial class MorePage : ContentPage
{
    private readonly DataViewModel _viewModel;
    private readonly Func<CategoriesPage> _categories;

    public MorePage(DataViewModel viewModel, Func<CategoriesPage> categories)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
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
}
