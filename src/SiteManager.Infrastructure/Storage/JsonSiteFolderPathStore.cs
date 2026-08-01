using System.Text.Json;
using SiteManager.Core.Storage;

namespace SiteManager.Infrastructure.Storage;

public sealed class JsonSiteFolderPathStore : ISiteFolderPathStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Dictionary<Guid, string> _paths = [];

    public JsonSiteFolderPathStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public static string GetDefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SiteManager",
        "site-folder-paths.json");

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_path))
            {
                _paths = [];
                return;
            }

            await using var input = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var values = await JsonSerializer.DeserializeAsync<Dictionary<Guid, string>>(input, SerializerOptions, cancellationToken);
            _paths = values ?? [];
        }
        finally
        {
            _gate.Release();
        }
    }

    public string? Get(Guid siteId) => _paths.TryGetValue(siteId, out var path) ? path : null;

    public async Task SetAsync(Guid siteId, string folderPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _paths[siteId] = folderPath;
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temporaryPath = $"{_path}.{Guid.NewGuid():N}.tmp";
            try
            {
                await using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                                 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await JsonSerializer.SerializeAsync(output, _paths, SerializerOptions, cancellationToken);
                    await output.FlushAsync(cancellationToken);
                }

                File.Move(temporaryPath, _path, overwrite: true);
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
        finally
        {
            _gate.Release();
        }
    }
}
