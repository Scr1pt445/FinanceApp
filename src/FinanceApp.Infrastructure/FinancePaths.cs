namespace FinanceApp.Infrastructure;

/// <summary>
/// Where the app keeps its data (ADR 0005): explicit directory → <c>FINANCEAPP_DATA_DIR</c> →
/// <c>%LocalAppData%\FinanceApp</c>. Each Windows user gets their own data.
/// </summary>
public sealed class FinancePaths
{
    public const string DataDirectoryVariable = "FINANCEAPP_DATA_DIR";

    public FinancePaths(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        DataDirectory = Path.GetFullPath(dataDirectory);
    }

    public string DataDirectory { get; }

    public string DatabasePath => Path.Combine(DataDirectory, "finance.db");

    public string SettingsPath => Path.Combine(DataDirectory, "settings.json");

    public string ConnectionString => $"Data Source={DatabasePath};Foreign Keys=True;Pooling=False";

    public static FinancePaths Resolve(string? explicitDirectory = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitDirectory))
        {
            return new FinancePaths(explicitDirectory);
        }

        var fromEnvironment = Environment.GetEnvironmentVariable(DataDirectoryVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return new FinancePaths(fromEnvironment);
        }

        return new FinancePaths(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FinanceApp"));
    }

    public void EnsureDataDirectory() => Directory.CreateDirectory(DataDirectory);
}
