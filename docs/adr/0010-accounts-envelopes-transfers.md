# 0010. Accounts, envelopes and transfers

Date: 2026-10-16 · Status: Accepted · Supersedes [0003](0003-no-transfers-in-mvp.md)

## Context

The first version only recorded income, expenses and "savings" against loose accounts, which made the app little more than a data feed for Obsidian. Inside the app the user wants to work like in a banking app: keep a few accounts (card, cash), see what is on them, pay from them, move money between them — and keep **envelopes**: money put aside for a purpose («Машина», «Велосипед», «Отдых»), the way people keep cash envelopes in real life.

Accounts are a convenience, not a mirror of real banks: nobody has to reconcile them with a bank statement.

## Decision

- **An envelope is an account of a special kind** (`AccountKind.Envelope`), like the piggy banks of banking apps: it has its own balance; money moves in and out by transfers. It can also be spent from directly («купил велосипед» — an expense from the envelope).
- **Transfers are a transaction type.** Types are now `Income`, `Expense` and `Transfer`. A transfer has a source account, a destination account, the amount in the source currency and the amount received in the destination currency (equal for the same currency; for different currencies it defaults to the CBR rate and can be corrected). A transfer has no category.
- **«Saving» is no longer a type.** Putting money aside is a transfer into an envelope. Existing saving transactions are converted by a migration into transfers into envelopes named after their category («Копилка», «Инвестиции»); the same happens when importing legacy CSV files.
- **Balances.** `balance = optional opening balance + income − expenses ± transfers`, in the account currency. The opening balance is optional (0 by default) — accounts are not expected to match a bank.
- **Goals are optional** and only for envelopes: a target amount and an optional date. Nothing about goals is shown unless a goal is set.
- **Summaries:**
  - *expenses* include expenses from envelopes (it is still spending);
  - *into envelopes* = transfers from regular accounts into envelopes − transfers back;
  - *balance* (how much free money grew) = income − expenses from regular accounts − into envelopes. An expense paid from an envelope does not reduce free money a second time: it was already put aside.
  - Transfers between two regular accounts or two envelopes do not change any total.

## Consequences

- A schema migration adds the account kind, opening balance, goal, and the transfer columns (`ToAccountId`, `ToAmount`); the category becomes optional (required for income and expense only).
- The quick-entry `>` prefix now means "put into an envelope": `>10000 машина` transfers from the default account into the envelope «Машина».
- The CSV export changes (ADR 0011).
