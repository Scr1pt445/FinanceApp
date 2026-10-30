# 0009. Quick-entry syntax

Date: 2026-10-16 · Status: Accepted, amended by [0010](0010-accounts-envelopes-transfers.md): `>` now moves money into an envelope

## Context

The quick-entry window has a single input line; recording a transaction must take about five seconds. The line has to carry the type, amount, category and note, and optionally the date and the account, without forms or dropdowns. The UI language is Russian.

## Decision

```
[+|>]<amount> [<category>] [<note>…] [<date>] [@<account>]
```

| Part | Rule | Examples |
|---|---|---|
| Type | No prefix — expense, `+` — income, `>` — a transfer into an envelope (ADR 0010). The prefix may be attached to the amount or stand alone. | `350`, `+50000`, `> 10000 машина` |
| Amount | Positive, comma or dot as decimal separator, at most two decimals; thousands may be separated by spaces. | `350`, `350,50`, `1 200` |
| Category | The first word(s) after the amount, matched by prefix against visible categories **of that type**, case-insensitive; an exact name wins over a prefix. If no category is named, it is suggested from previous notes (`кофе` → «Еда»). After `>` the word names the envelope instead. | `ед`, `тр`, `пассивный доход` |
| Date | Anywhere after the amount: `сегодня`, `вчера`, `позавчера`; a weekday (`пн`…`вс` or full name) — the most recent such day, today included; `ДД.ММ` (this year, or last year if that date is still ahead) or `ДД.ММ.ГГГГ`. Default — today. | `вчера`, `пт`, `12.10` |
| Account | `@` followed by the beginning of the account name; multi-word names are matched greedily and an exact name wins. Default — the last base-currency account used **for the same type**; foreign-currency accounts are only used when named. | `@нал`, `@карта usd` |
| Currency | Always the currency of the account (ADR 0002); there is no separate currency syntax. | `20 еда ужин @карта usd` |
| Note | Every remaining word, in order. | |

The type is never inferred from the category: `50000 зарплата` is an expense with an unknown category, not an income. This keeps the rule predictable — one glance at the first character tells the type.

## Consequences

- The parser is a pure function of the line, the catalogs, the note history and today's date, so it is fully unit-testable and can run on every keystroke to preview the result.
- Ambiguities are reported, not guessed: an `@` that matches several accounts or a category prefix that matches several categories is shown to the user before saving.
- Foreign-currency entry needs a dedicated account per currency, which matches how the core stores money.
- The default account is tracked per type and in the base currency only. A plain "last used account" turned out to be a trap: after `>10000 машина @вклад` the next expense defaulted to the deposit. Envelopes are never the default account, and after `20 еда @карта usd` the next expense would silently be in dollars.

## Amendment (2026-10-30): the receipt's currency

A currency right after the amount — `20 eur`, `20€`, `€20`, `20 евро`, a common ISO code or symbol — is the receipt's, for a card in one currency paying in another abroad. When it differs from the account's, the amount is converted into the account currency on saving, at what the bank charged if the form is told so, otherwise at the CBR rate of the date, and «20 EUR» is added to the note. Only the token right after the amount counts, so three letters elsewhere stay words of the note; a `>` line into an envelope stays in the account currency.
