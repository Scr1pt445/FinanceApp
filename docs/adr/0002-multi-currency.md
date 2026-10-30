# 0002. Multiple currencies, CBR rate fixed on the transaction date

Date: 2026-10-15 · Status: Accepted

## Context

Users hold money in several currencies (RUB, USD, EUR, BYN). Monthly totals — and any tool reading the CSV export — work in a single currency.

## Decision

- The app has a **base currency** (a setting, RUB by default).
- A transaction's currency is the currency of its **account**.
- A transaction stores the amount in that currency and the **exchange rate to the base currency, fixed on the transaction date**. Base amount = `round(Amount × ExchangeRate, 2)`.
- Rates come from `cbr-xml-daily.ru` (a mirror of the Central Bank of Russia rates) and are cached in the database. The CBR does not publish on weekends and holidays, so the last published rate is used. Offline, the latest cached rate is used and flagged as a fallback. The rate can always be entered manually.
- For a non-ruble base currency, a cross rate via the ruble is used.
- **The user's own rates** (added 2026-10-30, for the phone abroad): a rate «1 FROM = x TO» for a day, a week, a month or until removed, kept in the settings. While in force it wins over the CBR rate, either way round and as a leg of a cross rate via the ruble; without any CBR rate, a pair is also converted through a common currency of two such rates. Overlapping rates of one pair: the newest wins.

## Consequences

- Totals of past months never change when rates move.
- The app works offline; a currency that has never been cached needs a manual rate when there is no network — typed once for a period, not for every operation.
