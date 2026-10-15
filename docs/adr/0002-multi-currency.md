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

## Consequences

- Totals of past months never change when rates move.
- The app works offline; a currency that has never been cached needs a manual rate when there is no network.
