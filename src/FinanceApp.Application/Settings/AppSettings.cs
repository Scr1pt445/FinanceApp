using FinanceApp.Domain;

namespace FinanceApp.Application.Settings;

/// <summary>User settings, stored as JSON next to the database.</summary>
public sealed record AppSettings
{
    /// <summary>Currency of all totals. Chosen once: stored exchange rates are relative to it.</summary>
    public CurrencyCode BaseCurrency { get; init; } = CurrencyCode.Rub;

    public CsvExportSettings CsvExport { get; init; } = new();

    /// <summary>Global hotkey for the quick-entry window, e.g. «Ctrl+Alt+M». Changed in the desktop app.</summary>
    public string QuickEntryHotKey { get; init; } = DefaultQuickEntryHotKey;

    public const string DefaultQuickEntryHotKey = "Ctrl+Alt+M";

    /// <summary>Currency the «Всего» totals are shown in, at today's rate; <c>null</c> — the base currency.</summary>
    public CurrencyCode? TotalsCurrency { get; init; }
}

/// <summary>Optional CSV export (e.g. for the Obsidian «Финансы» page, ADR 0004). Off by default.</summary>
public sealed record CsvExportSettings
{
    public bool Enabled { get; init; }

    /// <summary>Full path of the CSV file to keep up to date.</summary>
    public string? Path { get; init; }

    public bool IsActive => Enabled && !string.IsNullOrWhiteSpace(Path);
}

public interface ISettingsStore
{
    /// <summary>Current settings; defaults if nothing was saved yet.</summary>
    AppSettings Load();

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
