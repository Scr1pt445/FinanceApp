# 0004. Standalone app; CSV export is an optional integration

Date: 2026-10-15 · Status: Accepted

## Context

The first use case was feeding a finance dashboard in Obsidian, which reads a CSV file. But the app is meant for other people too, most of whom do not use Obsidian.

## Decision

- The app is complete on its own: transaction list, summaries and catalogs do not depend on any external tool.
- The CSV export (contract in [requirements, section 5](../requirements.md#5-csv-export-contract)) is an **integration** that is enabled in settings and is **off by default**. The user chooses the file path; no personal paths live in code or in the repository.
- The export subscribes to data changes and runs in the background. A failed write (file locked) is retried and surfaces as a status, but never breaks entering a transaction.

## Consequences

The core does not know about Obsidian: it publishes a "data changed" notification and the integration reacts to it. Other exports can be added the same way.
