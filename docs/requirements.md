# FinanceApp — Product Requirements

Version 1.0 · 2026-10-14

Section numbers are stable: code comments refer to them (e.g. "requirements, section 5").

---

## 1. Purpose

A desktop app for personal finance tracking that people actually keep using:

- **Entering a transaction takes about five seconds.** Slow entry is the main reason people stop tracking.
- **Monthly totals at a glance:** income, expenses, savings and what is left.
- **Open data:** transactions can be exported to a plain CSV file that other tools (spreadsheets, note-taking apps such as Obsidian) can read.

## 2. Users and platform

- Single user per installation, no accounts, no cloud.
- Windows desktop.
- Works fully offline; data is stored locally.

## 3. MVP features

### 3.1. Quick entry

- A small entry window opens with a **global hotkey** from any application (default `Ctrl+Alt+M`) and from the tray icon.
- One input line with parsing ([ADR 0009](adr/0009-quick-entry-syntax.md)), for example:
  - `350 еда кофе` → expense 350, category «Еда», note «кофе»;
  - `350 кофе` → the category is suggested from previous notes;
  - `+50000 зарплата` → income;
  - `>10000 копилка` → saving;
  - `1200 тр такси вчера` → «Транспорт» by prefix, dated yesterday;
  - `350,50 еда обед 12.10` → decimal comma, dated 12 October;
  - `20 еда ужин @карта usd` → account «Карта USD», amount in dollars.
- The category is matched by prefix and suggested from previous notes (*кофе* → *Еда*).
- `Enter` saves and closes the window, `Esc` closes it.
- The default account is the last base-currency account used for the same type; foreign-currency accounts are chosen explicitly with `@`.

### 3.2. Transaction list

- Table with filters by month, type, category and account, plus search in notes.
- Edit and delete; delete can be undone (`Ctrl+Z`) or requires confirmation.

### 3.3. Summaries

- Current month: income, expenses, savings, balance (= income − expenses − savings).
- Expenses by category for the month (chart or bars).
- Comparison with the previous month.

### 3.4. Catalogs

- **Categories:** add, rename, hide. Each category belongs to one transaction type. Starter set (UI language is Russian):
  - income: Зарплата (salary), Фриланс (freelance), Репетиторство (tutoring), Пассивный доход (passive income), Прочее (other);
  - expense: Жильё (housing), Еда (food), Транспорт (transport), Здоровье (health), Развлечения (entertainment), Одежда (clothing), Образование (education), Работа (work: software, subscriptions), Связь (phone & internet), Прочее (other);
  - saving: Копилка (piggy bank), Инвестиции (investments).
- **Accounts:** Карта (card), Наличные (cash), Вклад (deposit); users add their own. Each account has a currency.

### 3.5. CSV export (optional integration)

Off by default. When enabled, the app keeps a CSV file up to date after every change. The format is a contract — see section 5.

### 3.6. Import

Import of CSV files in the same format, including files that only have the original six columns (e.g. data migrated from a previous tracking system), with per-month totals checked against the source file.

## 4. Data

### 4.1. Transaction

| Field | Type | Required | Notes |
|---|---|:-:|---|
| Id | GUID | ✓ | |
| Date | date | ✓ | no time of day; creation time is kept separately |
| Type | income / expense / saving | ✓ | |
| Amount | decimal | ✓ | always > 0, in the account currency; the type gives the sign |
| Currency | ISO 4217 code | ✓ | taken from the account |
| ExchangeRate | decimal | ✓ | to the base currency, fixed on the transaction date |
| Category | reference | ✓ | must match the type |
| Account | reference | ✓ | |
| Note | string | | |
| CreatedAt / UpdatedAt | UTC timestamp | ✓ | |

### 4.2. Storage

- Local **SQLite** database in the user's data folder.
- Money is `decimal` only, never floating point.
- Multiple currencies; totals are in a base currency chosen by the user (RUB by default).

## 5. CSV export contract

Columns, in this order:

```csv
date,type,amount,category,account,note,currency,original_amount,id
2026-10-07,expense,350,Еда,Карта,coffee,RUB,350,0b6c…
2026-10-07,income,50000,Зарплата,Карта,,RUB,50000,4f1a…
2026-10-08,saving,10000,Копилка,Вклад,,RUB,10000,9d2e…
```

Rules:

- UTF-8 **without BOM**, comma separator, LF line endings, header in the first line.
- `date` is strictly `YYYY-MM-DD`; `amount` is a positive number with a dot (`350.50`), always in the **base currency**.
- `type` is exactly `income`, `expense` or `saving`.
- Fields containing commas, quotes or line breaks are quoted; inner quotes are doubled (`"coffee, croissant"`).
- The first six columns are fixed: consumers rely on them. New columns may only be appended on the right; existing ones are never renamed or removed.

Writing:

- The database is the source of truth; the CSV is an export.
- After every change (add, edit, delete) the whole file is rewritten via a temporary file and an atomic replace, so readers never see a half-written file.
- If the file is busy, the write is retried after a short delay; the error is shown unobtrusively and never blocks entering a transaction.

## 6. Non-functional requirements

- The quick-entry window appears in **under 300 ms** after the hotkey (the app lives in the tray, the window is created in advance).
- Optional start with Windows.
- Automatic daily database backup, the last 14 kept.
- Dark theme by default (black background, purple accent `#9446f8`).
- Russian user interface.

## 7. Technology

| Layer | Choice |
|---|---|
| Language / runtime | C# / .NET 10 |
| UI | WPF, MVVM (CommunityToolkit.Mvvm) |
| Database | SQLite + EF Core |
| Global hotkey | WinAPI `RegisterHotKey` |
| Exchange rates | Central Bank of Russia (via cbr-xml-daily.ru) |
| Tests | xUnit |

## 8. MVP acceptance criteria

- [ ] Hotkey from any window → type `350 еда кофе` → `Enter`: saved and closed, all within ~5 seconds.
- [ ] With CSV export enabled, the change appears in the file within a couple of seconds.
- [ ] Importing a legacy CSV succeeds and per-month totals match the source file.
- [ ] Edits and deletions are reflected in the CSV.
- [ ] The input parser and the CSV export are covered by tests.
- [ ] README with description, screenshots and run instructions.

## 9. Milestones

1. **Core:** domain model, SQLite, CSV export and import, exchange rates, tests, CLI. No UI.
2. **Quick entry:** tray icon, global hotkey, entry window, input parser.
3. **Main window:** transaction list, filters, editing, monthly summary.
4. **Polish:** catalogs, settings, autostart, backups, README with screenshots.

## 10. Later (not in MVP)

- Category budgets with overspending warnings.
- Savings goals (amount, deadline, monthly contribution).
- Recurring payments (subscriptions, rent) with reminders.
- Entry via a Telegram bot.
- Bank statement import.
- Transfers between accounts.

## 11. Decisions

The open questions of the initial draft are resolved in [architecture decision records](adr/README.md):

- UI framework → WPF ([ADR 0001](adr/0001-ui-wpf.md)).
- Multiple currencies in MVP → yes, with the rate fixed on the transaction date ([ADR 0002](adr/0002-multi-currency.md), [ADR 0008](adr/0008-csv-contract-extension.md)).
- Transfers between accounts → not in MVP ([ADR 0003](adr/0003-no-transfers-in-mvp.md)).
- CSV export → optional integration, off by default ([ADR 0004](adr/0004-obsidian-export-is-optional.md)).
- Data location → `%LocalAppData%\FinanceApp`, no sync ([ADR 0005](adr/0005-data-location.md)).
- Target framework → .NET 10 ([ADR 0006](adr/0006-dotnet-10.md)); storage → EF Core + SQLite ([ADR 0007](adr/0007-ef-core-sqlite.md)).
- Quick-entry syntax → `+`/`>` type prefixes, dates as words or `ДД.ММ`, `@account`, currency from the account ([ADR 0009](adr/0009-quick-entry-syntax.md)).
