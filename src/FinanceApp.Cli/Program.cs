using System.CommandLine;
using System.Text;
using FinanceApp.Application.ExchangeRates;
using FinanceApp.Cli;
using FinanceApp.Domain;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

var dataDir = new Option<string?>("--data-dir")
{
    Description = "Каталог данных (по умолчанию %LocalAppData%\\FinanceApp или FINANCEAPP_DATA_DIR)",
    Recursive = true,
};

var root = new RootCommand("FinanceApp — учёт личных финансов (консольный инструмент)");
root.Options.Add(dataDir);

foreach (var command in Commands.All(dataDir))
{
    root.Subcommands.Add(command);
}

try
{
    // Expected errors (bad input, missing rate) are printed as one line below, not as a stack trace.
    return await root.Parse(args).InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });
}
catch (Exception ex) when (ex is DomainException or ExchangeRateUnavailableException or InvalidOperationException)
{
    Output.Error(ex.Message);
    return 1;
}
