using System.Text.Json;
using System.Text.Json.Serialization;
using Natee.Core.Interfaces;
using Natee.Core.Models;
using Natee.Core.Services;
using Natee.Infrastructure.Platform;

namespace Natee.Infrastructure.Persistence;

/// <summary>
/// Persists user settings as validated JSON in the user's local application-data directory.
/// </summary>
public sealed class JsonSettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string SettingsFilePath { get; }
    public NateeSettings CurrentSettings { get; private set; } = new();

    public JsonSettingsService(string? customSettingsFilePath = null)
    {
        SettingsFilePath = customSettingsFilePath ?? AppDataPaths.GetSettingsFilePath();
    }

    public async Task<NateeSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
            {
                CurrentSettings = new NateeSettings();
                await SaveAsync(CurrentSettings, cancellationToken);
                return CurrentSettings;
            }

            string json = await File.ReadAllTextAsync(SettingsFilePath, cancellationToken);
            var deserialized = JsonSerializer.Deserialize<NateeSettings>(json, SerializerOptions);
            var (sanitized, _) = SettingsValidator.ValidateAndNormalize(deserialized);
            CurrentSettings = sanitized;
            return CurrentSettings;
        }
        catch
        {
            CurrentSettings = new NateeSettings();
            return CurrentSettings;
        }
    }

    public async Task SaveAsync(NateeSettings settings, CancellationToken cancellationToken = default)
    {
        var (sanitized, _) = SettingsValidator.ValidateAndNormalize(settings);
        CurrentSettings = sanitized;

        string? dir = Path.GetDirectoryName(SettingsFilePath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string json = JsonSerializer.Serialize(sanitized, SerializerOptions);
        await File.WriteAllTextAsync(SettingsFilePath, json, cancellationToken);
    }
}
