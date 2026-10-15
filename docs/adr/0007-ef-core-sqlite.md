# 0007. EF Core + SQLite, money as `decimal`

Date: 2026-10-15 · Status: Accepted

## Context

The app needs a local database without a server. Money must never be stored as floating point. SQLite has no decimal type: EF Core stores `decimal` as `TEXT` and cannot translate aggregates over such columns to SQL.

## Decision

- SQLite with EF Core (migrations, LINQ, native `DateOnly`). The schema is migrated on startup.
- All amounts and rates are `decimal` in code and `TEXT` in the database, with no loss of precision.
- Totals are computed in memory over the transactions of the period (`SummaryCalculator`), not in SQL. Personal finance data is thousands of rows — this is fast and easy to test.

## Consequences

Sorting or filtering by amount in SQL is not possible, and not needed. If volumes grow, amounts can move to integer minor units (`INTEGER`) with a value converter, without touching the domain.
