using System.Text.Json;
using System.Text.Json.Serialization;
using FinanceApp.Application.Abstractions;
using FinanceApp.Application.Settings;
using FinanceApp.Domain;

namespace FinanceApp.Infrastructure.Settings;

/// <summary><c>settings.json</c> in the data directory. Missing file = defaults.</summary>
public sealed class JsonSettingsStore(FinancePaths paths, IFileWriter fileWriter) : ISettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new CurrencyCodeJsonConverter() },
    };

    private readonly Lock _lock = new();
    private AppSettings? _cached;

    public AppSettings Load()
    {
        lock (_lock)
        {
            if (_cached is not null)
            {
                return _cached;
            }

            if (!File.Exists(paths.SettingsPath))
            {
                return _cached = new AppSettings();
            }

            try
            {
                _cached = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(paths.SettingsPath), Options) ?? new AppSettings();
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"Файл настроек повреждён: {paths.SettingsPath}. {ex.Message}", ex);
            }

            return _cached;
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var json = JsonSerializer.SerializeToUtf8Bytes(settings, Options);
        await fileWriter.WriteAtomicallyAsync(paths.SettingsPath, json, cancellationToken);
        lock (_lock)
        {
            _cached = settings;
        }
    }

    private sealed class CurrencyCodeJsonConverter : JsonConverter<CurrencyCode>
    {
        public override CurrencyCode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            CurrencyCode.TryParse(reader.GetString(), out var code)
                ? code
                : throw new JsonException($"Некорректный код валюты: {reader.GetString()}");

        public override void Write(Utf8JsonWriter writer, CurrencyCode value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.Value);
    }
}
