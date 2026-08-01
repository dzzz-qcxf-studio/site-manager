using System.Text.Json;
using SiteManager.Core.Configuration;

namespace SiteManager.Infrastructure.Configuration;

public sealed class JsonSettingsStore : IServerProfileStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _settingsPath;

    public JsonSettingsStore(string settingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        _settingsPath = Path.GetFullPath(settingsPath);
    }

    public static string GetDefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SiteManager",
        "settings.json");

    public async Task<ServerProfile?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_settingsPath))
        {
            return null;
        }

        await using var input = new FileStream(
            _settingsPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var document = await JsonSerializer.DeserializeAsync<SettingsDocument>(input, SerializerOptions, cancellationToken)
            ?? throw new InvalidDataException("Settings file was empty.");
        if (document.SchemaVersion != 1 || document.Profile is null)
        {
            throw new InvalidDataException("Settings file has an unsupported schema.");
        }

        document.Profile.Validate();
        return document.Profile;
    }

    public async Task SaveAsync(ServerProfile profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.Validate();
        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = $"{_settingsPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var output = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             4096,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(output, new SettingsDocument(1, profile), SerializerOptions, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, _settingsPath, overwrite: true);
        }
        catch
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            throw;
        }
    }

    private sealed record SettingsDocument(int SchemaVersion, ServerProfile? Profile);
}
