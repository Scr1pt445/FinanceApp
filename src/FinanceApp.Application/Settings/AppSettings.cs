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

    public BackupSettings Backups { get; init; } = new();

    /// <summary>
    /// The phone's home screen shows the one-line quick entry. Off by default: many find buttons and forms
    /// easier than a syntax. The desktop app always has it, behind the hotkey.
    /// </summary>
    public bool QuickEntryLine { get; init; }
}

/// <summary>Daily copies of the database (ADR 0012). On by default: losing the only file loses everything.</summary>
public sealed record BackupSettings
{
    public const int DefaultKeep = 14;

    public const int MaxKeep = 365;

    public bool Enabled { get; init; } = true;

    /// <summary>How many latest copies to keep; older ones are deleted.</summary>
    public int Keep { get; init; } = DefaultKeep;
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
