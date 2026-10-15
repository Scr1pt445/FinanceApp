# 0009. Quick-entry syntax

Date: 2026-10-16 · Status: Accepted

## Context

The quick-entry window has a single input line; recording a transaction must take about five seconds. The line has to carry the type, amount, category and note, and optionally the date and the account, without forms or dropdowns. The UI language is Russian.

## Decision

```
[+|>]<amount> [<category>] [<note>…] [<date>] [@<account>]
```

| Part | Rule | Examples |
|---|---|---|
| Type | No prefix — expense, `+` — income, `>` — saving. The prefix may be attached to the amount or stand alone. | `350`, `+50000`, `> 10000` |
| Amount | Positive, comma or dot as decimal separator, at most two decimals. | `350`, `350,50`, `0.99` |
| Category | The first word(s) after the amount, matched by prefix against visible categories **of that type**, case-insensitive; an exact name wins over a prefix. If no category is named, it is suggested from previous notes (`кофе` → «Еда»). | `ед`, `тр`, `пассивный доход` |
| Date | Anywhere after the amount: `сегодня`, `вчера`, `позавчера`; a weekday (`пн`…`вс` or full name) — the most recent such day, today included; `ДД.ММ` (this year, or last year if that date is still ahead) or `ДД.ММ.ГГГГ`. Default — today. | `вчера`, `пт`, `12.10` |
| Account | `@` followed by the beginning of the account name; multi-word names are matched greedily and an exact name wins. Default — the last used account. | `@нал`, `@карта usd` |
| Currency | Always the currency of the account (ADR 0002); there is no separate currency syntax. | `20 еда ужин @карта usd` |
| Note | Every remaining word, in order. | |

The type is never inferred from the category: `50000 зарплата` is an expense with an unknown category, not an income. This keeps the rule predictable — one glance at the first character tells the type.

## Consequences

- The parser is a pure function of the line, the catalogs, the note history and today's date, so it is fully unit-testable and can run on every keystroke to preview the result.
- Ambiguities are reported, not guessed: an `@` that matches several accounts or a category prefix that matches several categories is shown to the user before saving.
- Foreign-currency entry needs a dedicated account per currency, which matches how the core stores money.
