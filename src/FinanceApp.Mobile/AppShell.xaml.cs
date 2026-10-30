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

    private readonly TabBar _tabBar = new();
    private readonly Dictionary<string, ShellSection> _tabs = [];

    public AppShell(HomePage home, OperationsPage operations, AccountsPage accounts, SummaryPage summary, MorePage more)
    {
        InitializeComponent();
        Add("Главная", "tab_home.png", HomeRoute, home);
        Add("Операции", "tab_operations.png", OperationsRoute, operations);
        Add("Счета", "tab_accounts.png", AccountsRoute, accounts);
        Add("Итоги", "tab_summary.png", SummaryRoute, summary);
        Add("Ещё", "tab_more.png", MoreRoute, more);
        Items.Add(_tabBar);
    }

    /// <summary>The tab with this route, e.g. <see cref="OperationsRoute"/>.</summary>
    public ShellSection this[string route] => _tabs[route];

    /// <summary>Switches tabs directly: no route matching through the sections Shell creates around the pages.</summary>
    public void Show(string route) => _tabBar.CurrentItem = _tabs[route];

    private void Add(string title, string icon, string route, Page page)
    {
        ShellSection section = new ShellContent { Title = title, Icon = icon, Route = route, Content = page };
        section.Title = title;
        section.Icon = icon;
        _tabs[route] = section;
        _tabBar.Items.Add(section);
    }
}
