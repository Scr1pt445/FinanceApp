# 0012. Daily database backups

Date: 2026-10-16 · Status: Accepted

## Context

All data lives in one SQLite file (ADR 0005, 0007). A broken disk sector, an accidental delete or a sync tool overwriting the file would lose years of records. The CSV export (ADR 0004) is optional and off by default, so it cannot be relied on as a backup, and it does not carry everything (exchange-rate cache, hidden categories).

## Decision

- On every start the desktop app makes **one copy a day** of `finance.db` into `backups\finance-yyyy-MM-dd_HHmmss.db` in the data directory, in the background. The user can also make a copy at any time from the settings window.
- Copies are made with SQLite's `VACUUM INTO`: a consistent snapshot taken by SQLite itself, safe while the app is writing, compacted, and a valid database on its own. The copy is written to a `.partial` file and renamed, so a half-written file never looks like a backup.
- The latest **14** copies are kept by default (1–365, configurable); older ones are deleted after each new copy. Only files matching the naming pattern are touched.
- Backups are **on by default** and can be turned off in the settings.
- Restoring is a manual step for now: close the app and replace `finance.db` with a copy. The settings window explains it and opens the folder.

## Consequences

- A copy costs one sequential read of the database — milliseconds for personal-finance volumes — and does not delay startup.
- Copies sit next to the database, so they protect against corruption and mistakes, not against losing the disk. Putting the data directory in a synced folder (`FINANCEAPP_DATA_DIR`) covers that.
- A restore command in the app may come later; the file format makes it a plain file swap.
