using System.CommandLine;
using System.Globalization;
using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Csv;
using FinanceApp.Application.Export;
using FinanceApp.Application.ExchangeRates;
using FinanceApp.Application.Import;
using FinanceApp.Application.Settings;
using FinanceApp.Application.Summaries;
using FinanceApp.Application.Transactions;
using FinanceApp.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceApp.Cli;

internal static class Commands
{
    public static IEnumerable<Command> All(Option<string?> dataDir)
    {
        yield return Init(dataDir);
        yield return Add(dataDir);
        yield return List(dataDir);
        yield return Delete(dataDir);
        yield return Summary(dataDir);
        yield return Import(dataDir);
        yield return Export(dataDir);
        yield return Rates(dataDir);
        yield return Accounts(dataDir);
        yield return Categories(dataDir);
        yield return Config(dataDir);
    }

    private static Command Init(Option<string?> dataDir)
    {
        var baseCurrency = new Option<string>("--base-currency") { Description = "Базовая валюта новой базы", DefaultValueFactory = _ => "RUB" };
        var command = new Command("init", "Создать базу данных со стартовыми категориями и счетами") { baseCurrency };
        command.SetAction(async (parse, ct) =>
        {
            await using var host = await CliHost.StartAsync(parse.GetValue(dataDir), CurrencyCode.Parse(parse.GetValue(baseCurrency)!), ct);
            Console.WriteLine($"Данные: {host.Paths.DataDirectory}");
            Console.WriteLine($"Базовая валюта: {host.Settings.BaseCurrency}");
            return 0;
        });
        return command;
    }

    private static Command Add(Option<string?> dataDir)
    {
        var amount = new Argument<decimal>("amount") { Description = "Сумма в валюте счёта, например 350.50" };
        var type = new Option<string>("--type", "-t") { Description = "income | expense | saving", DefaultValueFactory = _ => "expense" };
        var category = new Option<string>("--category", "-c") { Description = "Категория", Required = true };
        var account = new Option<string>("--account", "-a") { Description = "Счёт", DefaultValueFactory = _ => "Карта" };
        var date = new Option<string?>("--date", "-d") { Description = "Дата ГГГГ-ММ-ДД (по умолчанию сегодня)" };
        var note = new Option<string?>("--note", "-n") { Description = "Комментарий" };
        var rate = new Option<decimal?>("--rate") { Description = "Курс к базовой валюте вручную" };
        var command = new Command("add", "Добавить операцию") { amount, type, category, account, date, note, rate };
        command.SetAction(async (parse, ct) =>
        {
            await using var host = await CliHost.StartAsync(parse.GetValue(dataDir), null, ct);
            var transactionType = ParseType(parse.GetValue(type)!);
            var day = ParseDate(parse.GetValue(date));
            var saved = await host.InScopeAsync(async sp =>
            {
                var categoryEntity = await FindCategoryAsync(sp, parse.GetValue(category)!, transactionType, ct);
                var accountEntity = await FindAccountAsync(sp, parse.GetValue(account)!, ct);
                return await sp.GetRequiredService<TransactionService>().AddAsync(
                    new TransactionInput(day, transactionType, parse.GetValue(amount), categoryEntity.Id, accountEntity.Id, parse.GetValue(note), parse.GetValue(rate)),
                    ct);
            });

            var t = saved.Transaction;
            Console.WriteLine($"Сохранено: {t.Date:yyyy-MM-dd} {Output.TypeName(t.Type)} {Output.Money(t.Amount, t.Currency)} · id {t.Id}");
            if (t.Currency != host.Settings.BaseCurrency)
            {
                Console.WriteLine($"  Курс {t.ExchangeRate.ToString(CultureInfo.InvariantCulture)} → {Output.Money(t.BaseAmount, host.Settings.BaseCurrency)}{(saved.RateIsFallback ? " (последний известный курс — нет связи с ЦБ)" : "")}");
            }

            return 0;
        });
        return command;
    }

    private static Command List(Option<string?> dataDir)
    {
        var month = new Option<string?>("--month", "-m") { Description = "Месяц ГГГГ-ММ (по умолчанию текущий)" };
        var all = new Option<bool>("--all") { Description = "Все операции" };
        var command = new Command("list", "Список операций") { month, all };
        command.SetAction(async (parse, ct) =>
        {
            await using var host = await CliHost.StartAsync(parse.GetValue(dataDir), null, ct);
            var period = ParseMonth(parse.GetValue(month));
            var rows = await host.InScopeAsync(async sp =>
            {
                var transactions = parse.GetValue(all)
                    ? await sp.GetRequiredService<ITransactionRepository>().ListAsync(cancellationToken: ct)
                    : await sp.GetRequiredService<ITransactionRepository>().ListAsync(period.FirstDay, period.LastDay, ct);
                return CsvExportService.BuildRows(
                    transactions,
                    await sp.GetRequiredService<ICategoryRepository>().ListAsync(ct),
                    await sp.GetRequiredService<IAccountRepository>().ListAsync(ct));
            });

            foreach (var row in rows)
            {
                var original = row.Currency != host.Settings.BaseCurrency ? $" ({Output.Money(row.OriginalAmount!.Value, row.Currency!.Value)})" : "";
                Console.WriteLine($"{row.Date:yyyy-MM-dd}  {Output.TypeName(row.Type),-8} {Output.Money(row.Amount),12}{original}  {row.Category} · {row.Account}  {row.Note}  [{row.Id}]");
            }

            Console.WriteLine($"Всего: {rows.Count}");
            return 0;
        });
        return command;
    }

    private static Command Delete(Option<string?> dataDir)
    {
        var id = new Argument<Guid>("id") { Description = "Идентификатор операции (из list)" };
        var command = new Command("delete", "Удалить операцию") { id };
        command.SetAction(async (parse, ct) =>
        {
            await using var host = await CliHost.StartAsync(parse.GetValue(dataDir), null, ct);
            var deleted = await host.InScopeAsync(sp => sp.GetRequiredService<TransactionService>().DeleteAsync(parse.GetValue(id), ct));
            Console.WriteLine($"Удалено: {deleted.Date:yyyy-MM-dd} {Output.TypeName(deleted.Type)} {Output.Money(deleted.Amount, deleted.Currency)}");
            return 0;
        });
        return command;
    }

    private static Command Summary(Option<string?> dataDir)
    {
        var month = new Option<string?>("--month", "-m") { Description = "Месяц ГГГГ-ММ (по умолчанию текущий)" };
        var command = new Command("summary", "Итоги месяца") { month };
        command.SetAction(async (parse, ct) =>
        {
            await using var host = await CliHost.StartAsync(parse.GetValue(dataDir), null, ct);
            var period = ParseMonth(parse.GetValue(month));
            var summary = await host.InScopeAsync(async sp => SummaryCalculator.ForMonth(
                await sp.GetRequiredService<ITransactionRepository>().ListAsync(period.Previous.FirstDay, period.LastDay, ct),
                period,
                (await sp.GetRequiredService<ICategoryRepository>().ListAsync(ct)).ToDictionary(c => c.Id)));

            var currency = host.Settings.BaseCurrency;
            Output.Totals($"Итоги {period}:", summary.Current, currency);
            Console.WriteLine($"  Прошлый месяц: остаток {Output.Money(summary.Previous.Balance, currency)}, расходы {Output.Money(summary.Previous.Expense, currency)}");
            if (summary.ExpensesByCategory.Count > 0)
            {
                Console.WriteLine("Расходы по категориям:");
                foreach (var category in summary.ExpensesByCategory)
                {
                    var share = summary.ShareOf(category).ToString("P0", Output.Russian);
                    var change = category.Change == 0 ? "" : $" ({(category.Change > 0 ? "+" : "")}{Output.Money(category.Change)} к прошлому)";
                    Console.WriteLine($"  {category.CategoryName,-16} {Output.Money(category.Amount, currency),16} {share,5}{change}");
                }
            }

            return 0;
        });
        return command;
    }

    private static Command Import(Option<string?> dataDir)
    {
        var file = new Argument<FileInfo>("file") { Description = "CSV-файл (формат date,type,amount,category,account,note[,currency,original_amount,id])" };
        var defaultAccount = new Option<string>("--default-account") { Description = "Счёт для строк без счёта", DefaultValueFactory = _ => "Карта" };
        var dryRun = new Option<bool>("--dry-run") { Description = "Только проверить, ничего не менять" };
        var command = new Command("import", "Импортировать операции из CSV") { file, defaultAccount, dryRun };
        command.SetAction(async (parse, ct) =>
        {
            var source = parse.GetValue(file)!;
            if (!source.Exists)
            {
                Output.Error($"файл не найден: {source.FullName}");
                return 1;
            }

            await using var host = await CliHost.StartAsync(parse.GetValue(dataDir), null, ct);
            var text = await File.ReadAllTextAsync(source.FullName, ct);
            var report = await host.InScopeAsync(sp => sp.GetRequiredService<CsvImportService>().ImportAsync(
                text, new ImportOptions(parse.GetValue(defaultAccount)!, parse.GetValue(dryRun)), ct));

            if (!report.Succeeded)
            {
                Output.Error($"импорт отменён, ничего не изменено. Проблемные строки ({report.Errors.Count}):");
                foreach (var error in report.Errors)
                {
                    Console.Error.WriteLine($"  {error}");
                }

                return 1;
            }

            Console.WriteLine(report.DryRun ? "Проверка (dry run), база не изменена:" : "Импорт завершён:");
            Console.WriteLine($"  строк в файле: {report.RowsInFile}, добавлено: {report.Added}, обновлено: {report.Updated}, уже были: {report.SkippedDuplicates}");
            if (report.CreatedCategories.Count > 0)
            {
                Console.WriteLine($"  новые категории: {string.Join(", ", report.CreatedCategories)}");
            }

            if (report.CreatedAccounts.Count > 0)
            {
                Console.WriteLine($"  новые счета: {string.Join(", ", report.CreatedAccounts)}");
            }

            if (!report.DryRun)
            {
                await PrintMonthCheckAsync(host, report, ct);
            }

            return 0;
        });
        return command;
    }

    /// <summary>Acceptance check: per-month totals of the file equal the totals stored in the database.</summary>
    private static async Task PrintMonthCheckAsync(CliHost host, ImportReport report, CancellationToken ct)
    {
        var months = report.FileTotals.Keys.ToList();
        var stored = await host.InScopeAsync(async sp => SummaryCalculator.ByMonth(
            await sp.GetRequiredService<ITransactionRepository>().ListAsync(months[0].FirstDay, months[^1].LastDay, ct)));

        Console.WriteLine("Сверка по месяцам (файл → база):");
        var allMatch = true;
        foreach (var (month, fileTotals) in report.FileTotals)
        {
            var dbTotals = stored.GetValueOrDefault(month, MonthTotals.Empty);
            var match = dbTotals == fileTotals;
            allMatch &= match;
            Console.WriteLine($"  {month}  доходы {Output.Money(fileTotals.Income),12}  расходы {Output.Money(fileTotals.Expense),12}  отложено {Output.Money(fileTotals.Saving),12}  {(match ? "✓" : $"✗ в базе: {Output.Money(dbTotals.Income)} / {Output.Money(dbTotals.Expense)} / {Output.Money(dbTotals.Saving)}")}");
        }

        Console.WriteLine(allMatch
            ? "Суммы совпадают."
            : "Есть расхождения: в базе за эти месяцы есть операции не из этого файла.");
    }

    private static Command Export(Option<string?> dataDir)
    {
        var path = new Option<string?>("--path", "-p") { Description = "Куда записать CSV (по умолчанию путь из настроек)" };
        var command = new Command("export", "Записать CSV со всеми операциями") { path };
        command.SetAction(async (parse, ct) =>
        {
            await using var host = await CliHost.StartAsync(parse.GetValue(dataDir), null, ct);
            var target = parse.GetValue(path) ?? host.Settings.CsvExport.Path;
            if (string.IsNullOrWhiteSpace(target))
            {
                Output.Error("путь не указан: передайте --path или включите экспорт командой config export --enable --path <файл>");
                return 1;
            }

            var rows = await host.InScopeAsync(sp => sp.GetRequiredService<CsvExportService>().ExportAsync(target, ct));
            Console.WriteLine($"Записано {rows} операций в {Path.GetFullPath(target)}");
            return 0;
        });
        return command;
    }

    private static Command Rates(Option<string?> dataDir)
    {
        var currency = new Argument<string>("currency") { Description = "Код валюты, например USD" };
        var date = new Option<string?>("--date", "-d") { Description = "Дата ГГГГ-ММ-ДД (по умолчанию сегодня)" };
        var command = new Command("rates", "Курс валюты к базовой на дату (ЦБ РФ)") { currency, date };
        command.SetAction(async (parse, ct) =>
        {
            await using var host = await CliHost.StartAsync(parse.GetValue(dataDir), null, ct);
            var code = CurrencyCode.Parse(parse.GetValue(currency)!);
            var day = ParseDate(parse.GetValue(date));
            var quote = await host.InScopeAsync(sp => sp.GetRequiredService<IExchangeRateService>().GetRateAsync(code, host.Settings.BaseCurrency, day, ct));
            Console.WriteLine($"1 {quote.From} = {quote.Rate.ToString(CultureInfo.InvariantCulture)} {quote.To} на {quote.Date:yyyy-MM-dd}{(quote.IsFallback ? " (из кэша — нет связи с ЦБ)" : "")}");
            return 0;
        });
        return command;
    }

    private static Command Accounts(Option<string?> dataDir)
    {
        var command = new Command("accounts", "Счета");
        command.SetAction(async (parse, ct) =>
        {
            await using var host = await CliHost.StartAsync(parse.GetValue(dataDir), null, ct);
            foreach (var account in await host.InScopeAsync(sp => sp.GetRequiredService<IAccountRepository>().ListAsync(ct)))
            {
                Console.WriteLine($"{account.Name} ({account.Currency}){(account.IsHidden ? " — скрыт" : "")}");
            }

            return 0;
        });

        var name = new Argument<string>("name") { Description = "Название счёта" };
        var currency = new Option<string?>("--currency") { Description = "Валюта счёта (по умолчанию базовая)" };
        var add = new Command("add", "Добавить счёт") { name, currency };
        add.SetAction(async (parse, ct) =>
        {
            await using var host = await CliHost.StartAsync(parse.GetValue(dataDir), null, ct);
            var code = parse.GetValue(currency) is { } text ? CurrencyCode.Parse(text) : host.Settings.BaseCurrency;
            var account = await host.InScopeAsync(async sp =>
            {
                var repository = sp.GetRequiredService<IAccountRepository>();
                var existing = await repository.ListAsync(ct);
                if (existing.Any(a => string.Equals(a.Name, parse.GetValue(name)!.Trim(), StringComparison.CurrentCultureIgnoreCase)))
                {
                    throw new DomainException($"Счёт «{parse.GetValue(name)}» уже есть.");
                }

                var created = new Account(Guid.CreateVersion7(), parse.GetValue(name)!, code, existing.Count);
                repository.Add(created);
                await sp.GetRequiredService<IUnitOfWork>().SaveChangesAsync(ct);
                sp.GetRequiredService<DataChangedNotifier>().NotifyChanged();
                return created;
            });
            Console.WriteLine($"Добавлен счёт {account}");
            return 0;
        });
        command.Subcommands.Add(add);
        return command;
    }

    private static Command Categories(Option<string?> dataDir)
    {
        var command = new Command("categories", "Категории");
        command.SetAction(async (parse, ct) =>
        {
            await using var host = await CliHost.StartAsync(parse.GetValue(dataDir), null, ct);
            var categories = await host.InScopeAsync(sp => sp.GetRequiredService<ICategoryRepository>().ListAsync(ct));
            foreach (var group in categories.GroupBy(c => c.Type))
            {
                Console.WriteLine($"{Output.TypeName(group.Key)}: {string.Join(", ", group.Select(c => c.Name + (c.IsHidden ? " (скрыта)" : "")))}");
            }

            return 0;
        });
        return command;
    }

    private static Command Config(Option<string?> dataDir)
    {
        var command = new Command("config", "Настройки");

        var show = new Command("show", "Показать настройки");
        show.SetAction(async (parse, ct) =>
        {
            await using var host = await CliHost.StartAsync(parse.GetValue(dataDir), null, ct);
            var settings = host.Settings;
            Console.WriteLine($"Каталог данных:  {host.Paths.DataDirectory}");
            Console.WriteLine($"База данных:     {host.Paths.DatabasePath}");
            Console.WriteLine($"Базовая валюта:  {settings.BaseCurrency}");
            Console.WriteLine($"Экспорт CSV:     {(settings.CsvExport.Enabled ? "включён" : "выключен")}{(settings.CsvExport.Path is { } p ? $" → {p}" : "")}");
            return 0;
        });

        var enable = new Option<bool>("--enable") { Description = "Включить экспорт" };
        var disable = new Option<bool>("--disable") { Description = "Выключить экспорт" };
        var path = new Option<string?>("--path") { Description = "Файл CSV для экспорта" };
        var export = new Command("export", "Настроить экспорт CSV (например, для Obsidian)") { enable, disable, path };
        export.SetAction(async (parse, ct) =>
        {
            if (parse.GetValue(enable) == parse.GetValue(disable))
            {
                Output.Error("укажите ровно одно: --enable или --disable");
                return 1;
            }

            await using var host = await CliHost.StartAsync(parse.GetValue(dataDir), null, ct);
            var store = host.Get<ISettingsStore>();
            var current = store.Load();
            var newPath = parse.GetValue(path) is { } p ? Path.GetFullPath(p) : current.CsvExport.Path;
            if (parse.GetValue(enable) && string.IsNullOrWhiteSpace(newPath))
            {
                Output.Error("для включения нужен --path");
                return 1;
            }

            await store.SaveAsync(current with { CsvExport = new CsvExportSettings { Enabled = parse.GetValue(enable), Path = newPath } }, ct);
            if (parse.GetValue(enable))
            {
                var status = await host.Get<CsvExportCoordinator>().ExportNowAsync(ct);
                Console.WriteLine(status.State == CsvExportState.Succeeded
                    ? $"Экспорт включён, записано {status.Rows} операций в {newPath}"
                    : $"Экспорт включён, но запись не удалась: {status.Error}");
            }
            else
            {
                Console.WriteLine("Экспорт выключен.");
            }

            return 0;
        });

        command.Subcommands.Add(show);
        command.Subcommands.Add(export);
        return command;
    }

    private static TransactionType ParseType(string value) =>
        TransactionCsvFormat.TryParseType(value.Trim().ToLowerInvariant(), out var type)
            ? type
            : throw new DomainException($"Тип «{value}» не распознан: income, expense или saving.");

    private static DateOnly ParseDate(string? value) =>
        value is null
            ? DateOnly.FromDateTime(DateTime.Now)
            : TransactionCsvFormat.TryParseDate(value, out var date)
                ? date
                : throw new DomainException($"Дата «{value}» не распознана, нужен формат ГГГГ-ММ-ДД.");

    private static YearMonth ParseMonth(string? value) =>
        value is null ? YearMonth.Of(DateOnly.FromDateTime(DateTime.Now)) : YearMonth.Parse(value);

    private static async Task<Category> FindCategoryAsync(IServiceProvider sp, string name, TransactionType type, CancellationToken ct)
    {
        var categories = await sp.GetRequiredService<ICategoryRepository>().ListAsync(ct);
        return categories.FirstOrDefault(c => c.Type == type && string.Equals(c.Name, name.Trim(), StringComparison.CurrentCultureIgnoreCase))
            ?? throw new DomainException($"Категория «{name}» ({Output.TypeName(type)}) не найдена. Список: finance categories");
    }

    private static async Task<Account> FindAccountAsync(IServiceProvider sp, string name, CancellationToken ct)
    {
        var accounts = await sp.GetRequiredService<IAccountRepository>().ListAsync(ct);
        return accounts.FirstOrDefault(a => string.Equals(a.Name, name.Trim(), StringComparison.CurrentCultureIgnoreCase))
            ?? throw new DomainException($"Счёт «{name}» не найден. Список: finance accounts");
    }
}
