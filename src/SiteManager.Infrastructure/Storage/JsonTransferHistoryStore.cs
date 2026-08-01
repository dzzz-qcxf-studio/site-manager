using System.Text.Json;
using SiteManager.Core.Storage;
using SiteManager.Core.Transfers;

namespace SiteManager.Infrastructure.Storage;

public sealed class JsonTransferHistoryStore : ITransferHistoryStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonTransferHistoryStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public static string GetDefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SiteManager",
        "transfer-history.json");

    public async Task<IReadOnlyList<TransferHistoryEntry>> GetAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await ReadAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AppendAsync(TransferHistoryEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var entries = (await ReadAsync(cancellationToken)).ToList();
            entries.Insert(0, entry);
            await WriteAsync(entries.Take(100).ToArray(), cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<TransferHistoryEntry>> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        await using var input = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonSerializer.DeserializeAsync<List<TransferHistoryEntry>>(input, SerializerOptions, cancellationToken)
            ?? [];
    }

    private async Task WriteAsync(IReadOnlyList<TransferHistoryEntry> entries, CancellationToken cancellationToken)
    {
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
                await JsonSerializer.SerializeAsync(output, entries, SerializerOptions, cancellationToken);
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
}
