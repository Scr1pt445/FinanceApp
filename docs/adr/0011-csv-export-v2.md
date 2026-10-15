# 0011. CSV export v2: transfers and an accounts file

Date: 2026-10-16 · Status: Accepted · Amends [0008](0008-csv-contract-extension.md)

## Context

With transfers and envelopes (ADR 0010) the export must describe money moving between accounts and the state of accounts and envelopes. The consumer on the user's side becomes a dedicated Obsidian plugin instead of the original dashboard page, so the format may evolve — but stays documented, versioned and additive where possible.

## Decision

`transactions.csv` keeps all existing columns and their meaning and appends two:

```csv
date,type,amount,category,account,note,currency,original_amount,id,to_account,to_amount
2026-10-15,expense,350,Еда,Карта,кофе,RUB,350,01a1…,,
2026-10-15,transfer,10000,,Карта,,RUB,10000,01a1…,Машина,10000
```

- `type` is `income`, `expense` or `transfer`. `saving` is no longer written.
- For a transfer: `account` is the source, `to_account` the destination, `amount` is in the base currency, `original_amount` in the source currency and `to_amount` in the destination currency; `category` is empty.

A second file is written next to it, `accounts.csv`, rewritten atomically together with the first:

```csv
name,kind,currency,balance,hidden,goal_amount,goal_date
Карта,account,RUB,32900,false,,
Машина,envelope,RUB,95000,false,150000,2027-05-01
```

- `kind` is `account` or `envelope`; `balance` is in the account currency; goal columns are empty when no goal is set.

Import keeps accepting the original six-column format and `saving` rows: a saving row becomes a transfer from its account (or the default one) into an envelope named after its category.

## Consequences

- The original Obsidian dashboard page, which only knows `income`/`expense`/`saving`, must be replaced or adapted; the user covers this with a plugin.
- Consumers that ignore unknown columns keep reading income and expenses as before.
