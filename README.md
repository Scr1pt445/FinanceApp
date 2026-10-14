# FinanceApp

Десктопное приложение для учёта личных финансов на C# / .NET 10. Главное — **ввод операции за ~5 секунд** по глобальной горячей клавише. Ещё: итоги месяца, мультивалютность с курсами ЦБ РФ и необязательная выгрузка в CSV, например для страницы финансов в Obsidian.

> **Статус:** этап 1 из 4 — ядро без UI: модель, SQLite, курсы валют, экспорт и импорт CSV, консольный инструмент, тесты. Дальше — окно быстрого ввода на WPF ([этапы](docs/requirements.md#9-этапы)).

## Архитектура

Чистая архитектура: зависимости направлены внутрь, ядро не знает ни про UI, ни про SQLite, ни про Obsidian.

```
src/
  FinanceApp.Domain          сущности и инварианты: Transaction, Category, Account, CurrencyCode, YearMonth
  FinanceApp.Application     сценарии и порты: TransactionService, CsvImportService, CsvExportService,
                             SummaryCalculator, ExchangeRateService, CSV-контракт
  FinanceApp.Infrastructure  EF Core + SQLite, курсы ЦБ (cbr-xml-daily.ru), атомарная запись файлов,
                             settings.json, регистрация в DI
  FinanceApp.Cli             консольный инструмент `finance` — тот же граф сервисов, что будет у WPF
tests/
  FinanceApp.Core.Tests            чистые тесты домена и сценариев
  FinanceApp.Infrastructure.Tests  интеграционные тесты на настоящем SQLite и файловой системе
```

Ключевые решения описаны в [ADR](docs/adr/README.md). Коротко:

- **Деньги — только `decimal`.** Сумма хранится в валюте счёта, рядом — курс к базовой валюте, **зафиксированный на дату операции**. Поэтому итоги прошлых месяцев не меняются вслед за курсом ([ADR 0002](docs/adr/0002-multi-currency.md)).
- **Курсы ЦБ кэшируются**, без сети берётся последний известный курс, и это явно отмечается.
- **Экспорт CSV — интеграция, а не часть ядра.** Ядро сообщает «данные изменились», а координатор экспорта в фоне перезаписывает файл целиком: с debounce, через временный файл и `File.Replace`, с повторами, если файл занят. Ошибка экспорта не ломает ввод операции ([ADR 0004](docs/adr/0004-obsidian-export-is-optional.md)).
- **CSV-контракт** совместим с форматом Obsidian: первые шесть колонок неизменны, валюта и `id` добавлены справа ([ADR 0008](docs/adr/0008-csv-contract-extension.md)).
- **Импорт идемпотентен:** строки с `id` обновляются, строки без `id` сопоставляются по содержимому с учётом повторов. Импорт выполняется целиком или не выполняется совсем, есть dry run.

## Запуск

Нужен [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
dotnet build
```

```bash
dotnet test
```

Консольный инструмент:

```bash
dotnet run --project src/FinanceApp.Cli -- --help
```

Данные лежат в `%LocalAppData%\FinanceApp` (`finance.db`, `settings.json`). Другой каталог можно задать через `--data-dir <путь>` или переменную `FINANCEAPP_DATA_DIR`.

## CLI: основные команды

| Команда | Что делает |
|---|---|
| `finance init [--base-currency RUB]` | создаёт базу со стартовыми категориями и счетами |
| `finance add 350 -c Еда -n "кофе"` | добавляет операцию (`-t income\|expense\|saving`, `-a` счёт, `-d` дата, `--rate` курс вручную) |
| `finance list [-m 2026-10] [--all]` | список операций |
| `finance summary [-m 2026-10]` | итоги месяца, расходы по категориям, сравнение с прошлым месяцем |
| `finance delete <id>` | удаляет операцию |
| `finance import <file.csv> [--default-account Карта] [--dry-run]` | импорт CSV со сверкой сумм по месяцам |
| `finance export [--path file.csv]` | записывает CSV со всеми операциями |
| `finance config export --enable --path <file.csv>` | включает автоматический экспорт после каждого изменения |
| `finance rates USD [-d 2026-10-07]` | курс ЦБ на дату |
| `finance accounts [add "Карта USD" --currency USD]` | счета |

## Формат CSV

```csv
date,type,amount,category,account,note,currency,original_amount,id
2026-10-07,expense,350,Еда,Карта,"кофе, круассан",RUB,350,01a1…
2026-10-07,expense,1714.23,Еда,Карта USD,ужин,USD,20,01a1…
```

UTF-8 без BOM, разделитель — запятая, переводы строк LF, `amount` — в базовой валюте с точкой. Подробнее — в [требованиях, раздел 5](docs/requirements.md#5-контракт-с-obsidian).

## Документы

- [Требования](docs/requirements.md)
- [Архитектурные решения (ADR)](docs/adr/README.md)
