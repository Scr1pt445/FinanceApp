using FinanceApp.Application.ExchangeRates;
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

    /// <summary>Rates the user typed for a day, a week, a month or for good; they win over the CBR rate.</summary>
    public IReadOnlyList<ManualRate> ManualRates { get; init; } = [];

    /// <summary>
    /// Currencies the user keeps in the choices besides the base one and those of the accounts (CurrencyList);
    /// <c>null</c> — never changed: rubles, dollars and euros.
    /// </summary>
    public IReadOnlyList<CurrencyCode>? Currencies { get; init; }

    /// <summary>The phone's PIN and fingerprint lock; off by default.</summary>
    public LockSettings Lock { get; init; } = new();

    /// <summary>Value equality, the list of rates included (a record compares lists by reference).</summary>
    public bool Equals(AppSettings? other) =>
        other is not null
        && BaseCurrency == other.BaseCurrency
        && CsvExport == other.CsvExport
        && QuickEntryHotKey == other.QuickEntryHotKey
        && TotalsCurrency == other.TotalsCurrency
        && Backups == other.Backups
        && QuickEntryLine == other.QuickEntryLine
        && Lock == other.Lock
        && ManualRates.SequenceEqual(other.ManualRates)
        && (Currencies is null ? other.Currencies is null : other.Currencies is not null && Currencies.SequenceEqual(other.Currencies));

    public override int GetHashCode() =>
        HashCode.Combine(BaseCurrency, CsvExport, QuickEntryHotKey, TotalsCurrency, Backups, QuickEntryLine, Lock, ManualRates.Count);
}

/// <summary>
/// The app opens only after the PIN (or a fingerprint, when allowed). Only a salted PBKDF2 hash of the PIN is
/// stored; the data itself is not encrypted — the lock keeps out someone holding an unlocked phone.
/// </summary>
public sealed record LockSettings
{
    public string? PinHash { get; init; }

    public string? PinSalt { get; init; }

    /// <summary>A fingerprint (or face) may open the app instead of the PIN.</summary>
    public bool Biometrics { get; init; }

    /// <summary>
    /// The home-screen widget shows the amounts even while the lock is on (anyone can look at a home screen, so off
    /// by default); its buttons work either way.
    /// </summary>
    public bool WidgetAmounts { get; init; }

    /// <summary>Wrong PINs in a row; kept on disk, so closing the app does not give five new tries.</summary>
    public int FailedAttempts { get; init; }

    /// <summary>No PIN is taken before this moment (UTC) after too many wrong ones.</summary>
    public DateTimeOffset? RetryAfter { get; init; }

    public bool IsOn => PinHash is not null && PinSalt is not null;
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
