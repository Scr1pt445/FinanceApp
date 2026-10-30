# 0013. A standalone Android app on .NET MAUI

Date: 2026-10-17 · Status: Accepted (2026-10-29, once the first APK built)

## Context

Most spending happens away from the computer, so the quickest moment to record it is on the phone. The desktop app is Windows-only (WPF, tray, global hotkey), but everything below the UI is plain .NET 10: the domain, use cases, the quick-entry parser, summaries and insights, EF Core with SQLite, CBR exchange rates, CSV export and import, backups. View models in `FinanceApp.Presentation` do not reference WPF and are covered by tests.

The app is meant for other people too, so the phone version has to work on its own: install, record, see where the money went — without a computer or an account.

## Decision

- **A separate Android app** in a new project `FinanceApp.Mobile`, built with **.NET MAUI** on .NET 10. iOS is not planned for now; MAUI keeps that door open.
- **Its own local SQLite database, not connected to the desktop app.** No sync and no server: ADR 0005 stays as is. Each install is a complete app, like the desktop one.
- **Reuse, not port.** `FinanceApp.Domain`, `FinanceApp.Application` and `FinanceApp.Infrastructure` are referenced as they are; Windows-specific parts (the data directory under `%LocalAppData%`, `HKCU` autostart, the tray and the hotkey) stay in the desktop project. The data directory comes from the platform (`FileSystem.AppDataDirectory`) through the existing `FinancePaths`.
- **Phone-first screens**, not a copy of the desktop window: accounts and envelopes, operations by day, add an expense, income or transfer, summaries, categories, settings. View models are shared where the flow is the same and written anew where a phone needs a different one.
- **Quick entry stays the core idea:** the same one-line syntax (`350 еда кофе`, ADR 0009), opened from an app shortcut and later from a home-screen widget.
- **Data leaves the phone through files:** CSV export (ADR 0011) and database copies (ADR 0012), shared with the Android share sheet. The existing idempotent import can bring that CSV into the desktop app by hand.
- **Distribution:** a signed APK in GitHub Releases first; Google Play once the app is stable.

## Plan

| Step | Scope | Status |
|---|---|:-:|
| 5.1 | MAUI project, the core running on Android: database, starter catalog, rates, the quick-entry line | ✅ |
| 5.2 | Accounts and envelopes, operations by day, forms for expense, income and transfer | ✅ |
| 5.3 | Summaries and insights, categories, settings, export and backups through sharing | ✅ ¹ |
| 5.4 | Icon, signed release APK, README with phone screenshots | |

After 5.3 the phone also got (2026-10-30): restoring a copy, a PIN and fingerprint lock, shortcuts on the app icon and a home-screen widget (Android-only code under `Platforms/Android`), recurring operations and monthly limits per category (in the shared core, migration `RecurringOperationsAndLimits`).

¹ The phone has no settings screen: the base currency is chosen once, on the first run (with the starting balances); the totals currency on the accounts tab; daily backups are always on with the default of 14 copies.

## Consequences

- Most of the code and its tests serve both apps; a fix in the summary math or the parser reaches both.
- `FinanceApp.Infrastructure` must not grow Windows-only dependencies; anything platform-specific goes behind an interface implemented per app.
- Two devices mean two separate sets of data. A user who wants one budget across phone and computer needs sync — a later decision (transport through the user's own cloud storage is the likely option; GUID keys and `UpdatedAt` already allow merging, deletions would need tombstones).
- MAUI adds a workload and an Android SDK to the build, so the desktop solution keeps building without them: the mobile project is built separately.
