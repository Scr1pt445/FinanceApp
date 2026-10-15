# 0005. Data in `%LocalAppData%\FinanceApp`, no sync

Date: 2026-10-15 · Status: Accepted

## Context

Each person who installs the app has their own data. Writing next to the executable fails when the app is installed under `Program Files`. A SQLite file inside a synced folder (cloud drive, note sync) risks corruption on concurrent writes.

## Decision

- The default data directory is `%LocalAppData%\FinanceApp` (`finance.db`, `settings.json`, later backups).
- It can be overridden with the `FINANCEAPP_DATA_DIR` environment variable or the `--data-dir` option (CLI and tests).
- The database is not synchronized. Data leaves the machine only through exports and backups.

## Consequences

Every Windows user gets their own data with zero configuration. A shared budget across several machines would need a separate solution (a server or sync); GUID keys and `UpdatedAt` in the model already allow it.
