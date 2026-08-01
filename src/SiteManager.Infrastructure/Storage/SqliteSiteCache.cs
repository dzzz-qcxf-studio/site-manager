using System.Text.Json;
using Microsoft.Data.Sqlite;
using SiteManager.Core.Models;
using SiteManager.Core.Storage;
using SiteManager.Core.Transfers;

namespace SiteManager.Infrastructure.Storage;

public sealed class SqliteSiteCache : ISiteCache, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new();
    private readonly string _connectionString;

    public SqliteSiteCache(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        var fullPath = Path.GetFullPath(databasePath);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Pooling = false
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS schema_info (version INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS sites (id TEXT PRIMARY KEY, json TEXT NOT NULL, updated_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS transfer_checkpoints (
                request_id TEXT PRIMARY KEY, upload_id TEXT NOT NULL, site_id TEXT NULL,
                archive_path TEXT NOT NULL, remote_path TEXT NOT NULL, expected_sha256 TEXT NOT NULL,
                total_bytes INTEGER NOT NULL, remote_offset INTEGER NOT NULL, updated_at TEXT NOT NULL
            );
            """, cancellationToken);

        await using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "SELECT version FROM schema_info LIMIT 1;";
        var value = await versionCommand.ExecuteScalarAsync(cancellationToken);
        if (value is null)
        {
            await ExecuteAsync(connection, "INSERT INTO schema_info(version) VALUES (1);", cancellationToken);
        }
        else if (Convert.ToInt32(value) != 1)
        {
            throw new InvalidOperationException("Unsupported local cache schema version.");
        }
    }

    public async Task<int> GetSchemaVersionAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT version FROM schema_info LIMIT 1;";
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null ? 0 : Convert.ToInt32(value);
    }

    public async Task<IReadOnlyList<SiteManifest>> GetSitesAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT json FROM sites ORDER BY updated_at DESC;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var sites = new List<SiteManifest>();
        while (await reader.ReadAsync(cancellationToken))
        {
            sites.Add(JsonSerializer.Deserialize<SiteManifest>(reader.GetString(0), JsonOptions)
                ?? throw new InvalidDataException("Cached site was invalid."));
        }

        return sites;
    }

    public async Task ReplaceSitesAsync(IReadOnlyCollection<SiteManifest> sites, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sites);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await ExecuteAsync(connection, "DELETE FROM sites;", cancellationToken, transaction);
            foreach (var site in sites)
            {
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "INSERT INTO sites(id, json, updated_at) VALUES ($id, $json, $updatedAt);";
                command.Parameters.AddWithValue("$id", site.Id.ToString());
                command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(site, JsonOptions));
                command.Parameters.AddWithValue("$updatedAt", site.UpdatedAt.ToUniversalTime().ToString("O"));
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task SaveCheckpointAsync(TransferCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO transfer_checkpoints(request_id, upload_id, site_id, archive_path, remote_path, expected_sha256, total_bytes, remote_offset, updated_at)
            VALUES ($requestId, $uploadId, $siteId, $archivePath, $remotePath, $expectedSha256, $totalBytes, $remoteOffset, $updatedAt)
            ON CONFLICT(request_id) DO UPDATE SET upload_id = excluded.upload_id, site_id = excluded.site_id,
                archive_path = excluded.archive_path, remote_path = excluded.remote_path, expected_sha256 = excluded.expected_sha256,
                total_bytes = excluded.total_bytes, remote_offset = excluded.remote_offset, updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$requestId", checkpoint.RequestId.ToString());
        command.Parameters.AddWithValue("$uploadId", checkpoint.UploadId.ToString());
        command.Parameters.AddWithValue("$siteId", checkpoint.SiteId?.ToString() ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$archivePath", checkpoint.ArchivePath);
        command.Parameters.AddWithValue("$remotePath", checkpoint.RemotePath);
        command.Parameters.AddWithValue("$expectedSha256", checkpoint.ExpectedSha256);
        command.Parameters.AddWithValue("$totalBytes", checkpoint.TotalBytes);
        command.Parameters.AddWithValue("$remoteOffset", checkpoint.RemoteOffset);
        command.Parameters.AddWithValue("$updatedAt", checkpoint.UpdatedAt.ToUniversalTime().ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<TransferCheckpoint?> GetCheckpointAsync(Guid requestId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT upload_id, site_id, archive_path, remote_path, expected_sha256, total_bytes, remote_offset, updated_at FROM transfer_checkpoints WHERE request_id = $requestId;";
        command.Parameters.AddWithValue("$requestId", requestId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new TransferCheckpoint(
            requestId,
            Guid.Parse(reader.GetString(0)),
            reader.IsDBNull(1) ? null : Guid.Parse(reader.GetString(1)),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetInt64(5),
            reader.GetInt64(6),
            DateTimeOffset.Parse(reader.GetString(7)));
    }

    public async Task DeleteCheckpointAsync(Guid requestId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM transfer_checkpoints WHERE request_id = $requestId;";
        command.Parameters.AddWithValue("$requestId", requestId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();
        var directory = Path.GetDirectoryName(new SqliteConnectionStringBuilder(_connectionString).DataSource);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken, SqliteTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
