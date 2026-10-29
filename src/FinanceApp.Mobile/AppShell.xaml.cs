using FinanceApp.Mobile.Views;

namespace FinanceApp.Mobile;

/// <summary>
/// Five tabs at the bottom, like a banking app. The pages come from DI and are created here, not by Shell from
/// templates: they all take view models, and this way a page that cannot be built fails in the tests, not on
/// the phone.
/// </summary>
public partial class AppShell : Shell
{
    public const string HomeRoute = "home";
    public const string OperationsRoute = "operations";
    public const string AccountsRoute = "accounts";
    public const string SummaryRoute = "summary";
    public const string MoreRoute = "more";

    public AppShell(HomePage home, OperationsPage operations, AccountsPage accounts, SummaryPage summary, MorePage more)
    {
        InitializeComponent();
        Items.Add(new TabBar
        {
            Items =
            {
                Tab("Главная", "tab_home.png", HomeRoute, home),
                Tab("Операции", "tab_operations.png", OperationsRoute, operations),
                Tab("Счета", "tab_accounts.png", AccountsRoute, accounts),
                Tab("Итоги", "tab_summary.png", SummaryRoute, summary),
                Tab("Ещё", "tab_more.png", MoreRoute, more),
            },
        });
    }

    private static ShellContent Tab(string title, string icon, string route, Page page) =>
        new() { Title = title, Icon = icon, Route = route, Content = page };
}
