# 0008. Extend the CSV contract with columns on the right

Date: 2026-10-15 · Status: Accepted

## Context

The original contract (`date,type,amount,category,account,note`) has no notion of currency, and consumers sum up `amount` in one currency. The contract allows appending new columns on the right.

## Decision

Export format:

```csv
date,type,amount,category,account,note,currency,original_amount,id
2026-10-07,expense,350,Еда,Карта,coffee,RUB,350,0b6c…
2026-10-08,expense,1845.30,Еда,Card USD,dinner,USD,20,4f1a…
```

- `amount` is always in the **base** currency, so consumers' totals stay correct.
- `currency` and `original_amount` are the account currency and the amount in it.
- `id` identifies the transaction and makes re-import free of duplicates.
- Numbers use the invariant culture with no exponent: whole numbers without a fractional part (`350`), others with two digits (`350.50`).
- UTF-8 without BOM, LF line endings, RFC 4180 quoting.

Import accepts both the original six-column format (legacy data) and the extended one. Columns are matched by name; unknown columns are ignored.

## Consequences

Existing consumers keep working unchanged. Export → import preserves everything, including currency and rate.
