# 0001. WPF for the desktop UI

Date: 2026-10-15 · Status: Accepted

## Context

The app targets Windows desktops only. Its key scenarios are a tray icon, a global hotkey (`RegisterHotKey`) and a quick-entry window that must appear in under 300 ms. The candidates were WPF and Avalonia.

## Decision

WPF with MVVM (CommunityToolkit.Mvvm).

## Consequences

- A mature ecosystem for tray icons and Win32 interop, and predictable show time for a pre-created window.
- WPF remains the most common desktop stack in .NET job requirements.
- No cross-platform support. To keep that door open, the domain, use cases and infrastructure do not depend on the UI: the WPF project is a thin layer over `FinanceApp.Application`.
