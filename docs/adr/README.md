# Architecture Decision Records

Short records of decisions that are expensive to reverse. Format: context → decision → consequences.

| # | Decision | Status |
|---|---|---|
| [0001](0001-ui-wpf.md) | WPF for the desktop UI | Accepted |
| [0002](0002-multi-currency.md) | Multiple currencies, CBR rate fixed on the transaction date | Accepted |
| [0003](0003-no-transfers-in-mvp.md) | No transfers between accounts in the MVP | Superseded by 0010 |
| [0004](0004-obsidian-export-is-optional.md) | Standalone app; CSV export is an optional integration | Accepted |
| [0005](0005-data-location.md) | Data in `%LocalAppData%\FinanceApp`, no sync | Accepted |
| [0006](0006-dotnet-10.md) | Target .NET 10 instead of .NET 8 | Accepted |
| [0007](0007-ef-core-sqlite.md) | EF Core + SQLite, money as `decimal` | Accepted |
| [0008](0008-csv-contract-extension.md) | Extend the CSV contract with columns on the right | Amended by 0011 |
| [0009](0009-quick-entry-syntax.md) | Quick-entry syntax | Accepted |
| [0010](0010-accounts-envelopes-transfers.md) | Accounts, envelopes and transfers | Accepted |
| [0011](0011-csv-export-v2.md) | CSV export v2: transfers and an accounts file | Accepted |
| [0012](0012-database-backups.md) | Daily database backups with `VACUUM INTO` | Accepted |
