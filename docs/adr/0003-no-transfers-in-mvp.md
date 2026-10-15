# 0003. No transfers between accounts in the MVP

Date: 2026-10-15 · Status: Superseded by [0010](0010-accounts-envelopes-transfers.md)

## Context

A transfer (e.g. withdrawing cash from a card) changes neither income nor expenses. The CSV contract only allows the types `income`, `expense` and `saving`.

## Decision

The MVP has no transfers. Transaction types are income, expense and saving.

## Consequences

Account balances are not tracked in the MVP. If transfers are needed later, they become a separate entity that is excluded from the CSV export, so the contract does not change.
