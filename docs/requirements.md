# FinanceApp — Product Requirements

Version 1.0 · 2026-10-14

Section numbers are stable: code comments refer to them (e.g. "requirements, section 5").

---

## 1. Purpose

A desktop app for personal finance tracking that people actually keep using:

- **Entering a transaction takes about five seconds.** Slow entry is the main reason people stop tracking.
- **Works like a banking app:** accounts and envelopes with balances, income, expenses and transfers between them.
- **Totals for any period** at a glance: income, expenses, money put aside and what is left.
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
  - `>10000 машина` → transfer from the default account into the envelope «Машина»;
  - `1200 тр такси вчера` → «Транспорт» by prefix, dated yesterday;
  - `350,50 еда обед 12.10` → decimal comma, dated 12 October;
  - `20 еда ужин @карта usd` → account «Карта USD», amount in dollars.
- The category is matched by prefix and suggested from previous notes (*кофе* → *Еда*).
- `Enter` saves and closes the window, `Esc` closes it.
- The default account is the last base-currency account used for the same type; foreign-currency accounts are chosen explicitly with `@`.

### 3.2. Accounts and envelopes ([ADR 0010](adr/0010-accounts-envelopes-transfers.md))

- **Accounts** are where money is paid from: Карта, Наличные, or anything the user creates. They are a convenience — no reconciliation with a bank is expected. An optional opening balance can be set; the balance is computed from transactions.
- **Envelopes** hold money put aside for a purpose («Машина», «Велосипед», «Отдых»). Money is moved into and out of an envelope by transfers; an expense can also be paid from an envelope directly. An envelope may have an optional goal (amount and date) with progress; without a goal nothing goal-related is shown.
- Accounts and envelopes can be created, renamed and hidden; each has a currency.

### 3.3. Main window

Banking-app layout: accounts and envelopes with balances on the left (with the total, split into free money and money in envelopes); on the right, the transactions of the selected account (or of all), grouped by day with day totals. Buttons «Расход», «Доход», «Перевод» open simple forms (no syntax): amount, category tiles, account, date shortcuts, note.

- Filters by type, category and account, plus search in notes.
- Edit and delete; delete is undone with `Ctrl+Z` (no confirmation dialogs).

### 3.4. Summaries

- **Any period:** week, month, quarter, year, a custom range or all time; arrows step to the neighbouring period.
- Income, expenses, money put into envelopes, balance (ADR 0010), average spending per day.
- Comparison with the previous period of the same length.
- Expenses by category; income and expenses by month for periods longer than a month.

### 3.5. Categories

Add, rename, hide. Each category belongs to income or expense. Starter set (UI language is Russian):
- income: Зарплата (salary), Фриланс (freelance), Репетиторство (tutoring), Пассивный доход (passive income), Прочее (other);
- expense: Жильё (housing), Еда (food), Транспорт (transport), Здоровье (health), Развлечения (entertainment), Одежда (clothing), Образование (education), Работа (work: software, subscriptions), Связь (phone & internet), Прочее (other).

Starter accounts: Карта, Наличные. No starter envelopes — the user creates their own.

### 3.6. CSV export (optional integration)

Off by default. When enabled, the app keeps a CSV file up to date after every change. The format is a contract — see section 5.

### 3.7. Import

Import of CSV files in the same format, including files that only have the original six columns (e.g. data migrated from a previous tracking system), with per-month totals checked against the source file.

## 4. Data

### 4.1. Transaction

| Field | Type | Required | Notes |
|---|---|:-:|---|
| Id | GUID | ✓ | |
| Date | date | ✓ | no time of day; creation time is kept separately |
| Type | income / expense / transfer | ✓ | |
| Amount | decimal | ✓ | always > 0, in the account currency; the type gives the sign |
| Currency | ISO 4217 code | ✓ | taken from the account |
| ExchangeRate | decimal | ✓ | to the base currency, fixed on the transaction date |
| Category | reference | income, expense | must match the type; none for transfers |
| Account | reference | ✓ | the source account for a transfer |
| ToAccount | reference | transfer | destination, different from the source |
| ToAmount | decimal | transfer | received amount in the destination currency |
| Note | string | | |
| CreatedAt / UpdatedAt | UTC timestamp | ✓ | |

### 4.2. Account

| Field | Type | Required | Notes |
|---|---|:-:|---|
| Id, Name | GUID, string | ✓ | name is unique |
| Kind | account / envelope | ✓ | |
| Currency | ISO 4217 code | ✓ | |
| OpeningBalance | decimal | ✓ | 0 by default |
| GoalAmount, GoalDate | decimal, date | | envelopes only, both optional |
| IsHidden, SortOrder | | ✓ | |

### 4.3. Storage

- Local **SQLite** database in the user's data folder.
- Money is `decimal` only, never floating point.
- Multiple currencies; totals are in a base currency chosen by the user (RUB by default).

## 5. CSV export contract

Version 2 ([ADR 0011](adr/0011-csv-export-v2.md)) adds transfers (`type` = `transfer`, columns `to_account`, `to_amount`) and a second file `accounts.csv` with account and envelope balances. The rules below hold for both files.

Columns of `transactions.csv` in version 1, in this order:

```csv
date,type,amount,category,account,note,currency,original_amount,id
2026-10-07,expense,350,Еда,Карта,coffee,RUB,350,0b6c…
2026-10-07,income,50000,Зарплата,Карта,,RUB,50000,4f1a…
2026-10-08,saving,10000,Копилка,Вклад,,RUB,10000,9d2e…
```

Rules:

- UTF-8 **without BOM**, comma separator, LF line endings, header in the first line.
- `date` is strictly `YYYY-MM-DD`; `amount` is a positive number with a dot (`350.50`), always in the **base currency**.
- `type` is exactly `income`, `expense` or `transfer` (version 1 also had `saving`, still accepted on import).
- Fields containing commas, quotes or line breaks are quoted; inner quotes are doubled (`"coffee, croissant"`).
- The first six columns keep their names and meaning. New columns are appended on the right; existing ones are never renamed or removed.

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
3. **Accounts and main window:** accounts, envelopes and transfers; banking-style main window with forms; summaries for any period.
4. **Polish:** catalogs, settings, autostart, backups, README with screenshots.

## 10. Later (not in MVP)

- Category budgets with overspending warnings.
- Recurring payments (subscriptions, rent) with reminders.
- Entry via a Telegram bot.
- Bank statement import.

## 11. Decisions

The open questions of the initial draft are resolved in [architecture decision records](adr/README.md):

- UI framework → WPF ([ADR 0001](adr/0001-ui-wpf.md)).
- Multiple currencies in MVP → yes, with the rate fixed on the transaction date ([ADR 0002](adr/0002-multi-currency.md), [ADR 0008](adr/0008-csv-contract-extension.md)).
- Transfers between accounts → first left out ([ADR 0003](adr/0003-no-transfers-in-mvp.md)), then added together with envelopes ([ADR 0010](adr/0010-accounts-envelopes-transfers.md)); export format v2 ([ADR 0011](adr/0011-csv-export-v2.md)).
- CSV export → optional integration, off by default ([ADR 0004](adr/0004-obsidian-export-is-optional.md)).
- Data location → `%LocalAppData%\FinanceApp`, no sync ([ADR 0005](adr/0005-data-location.md)).
- Target framework → .NET 10 ([ADR 0006](adr/0006-dotnet-10.md)); storage → EF Core + SQLite ([ADR 0007](adr/0007-ef-core-sqlite.md)).
- Quick-entry syntax → `+`/`>` type prefixes, dates as words or `ДД.ММ`, `@account`, currency from the account ([ADR 0009](adr/0009-quick-entry-syntax.md)).
