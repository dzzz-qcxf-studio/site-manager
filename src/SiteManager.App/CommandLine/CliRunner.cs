using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using SiteManager.Core.Configuration;
using SiteManager.Core.Models;
using SiteManager.Core.Publishing;
using SiteManager.Core.Storage;
using SiteManager.Core.Transfers;
using SiteManager.Infrastructure.Archives;
using SiteManager.Infrastructure.Configuration;
using SiteManager.Infrastructure.Ssh;
using SiteManager.Infrastructure.Storage;
using SiteManager.Core.Validation;
using SiteManager.App.ViewModels;

namespace SiteManager.App.CommandLine;

public static class CliExitCodes
{
    public const int Success = 0;
    public const int UsageError = 2;
    public const int OperationError = 3;
    public const int SelectionError = 4;
}

public sealed record CliError(string Code, string Message, object? Details = null);

public sealed record CliResponse(bool Ok, string Command, object? Data = null, CliError? Error = null);

internal sealed class CliOperationException(
    string code,
    string message,
    int exitCode,
    object? details = null) : Exception(message)
{
    public string Code { get; } = code;

    public int ExitCode { get; } = exitCode;

    public object? Details { get; } = details;
}

public sealed record CliSite(
    Guid Id,
    string Name,
    string Note,
    string Slug,
    SiteStatus Status,
    int Version,
    long SizeBytes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? TrashedAt,
    DateTimeOffset? PurgeAt,
    string Url);

public sealed class CliRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly Func<string, IServerProfileStore> _settingsStoreFactory;
    private readonly IRemotePublisherFactory _remotePublisherFactory;
    private readonly Func<string, ISiteCache> _cacheFactory;

    public CliRunner(
        Func<string, IServerProfileStore>? settingsStoreFactory = null,
        IRemotePublisherFactory? remotePublisherFactory = null,
        Func<string, ISiteCache>? cacheFactory = null)
    {
        _settingsStoreFactory = settingsStoreFactory ?? (path => new JsonSettingsStore(path));
        _remotePublisherFactory = remotePublisherFactory ?? new SshNetRemotePublisherFactory();
        _cacheFactory = cacheFactory ?? (path => new SqliteSiteCache(path));
    }

    public async Task<int> RunAsync(
        IReadOnlyList<string> args,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        CliInvocation? invocation = null;
        try
        {
            invocation = CommandLineParser.Parse(args);
            if (invocation.Version || invocation.Command is "version")
            {
                WriteSuccess(stdout, invocation, new { version = GetVersion() });
                return CliExitCodes.Success;
            }

            if (invocation.Help || invocation.Command is "help")
            {
                WriteHelp(stdout, invocation.Json);
                return CliExitCodes.Success;
            }

            var settingsPath = invocation.Get("settings") ?? JsonSettingsStore.GetDefaultPath();
            var profile = await _settingsStoreFactory(settingsPath).LoadAsync(cancellationToken);
            if (profile is null)
            {
                throw new CliOperationException(
                    "NOT_CONFIGURED",
                    "尚未配置服务器连接。请先在桌面端“设置”中完成 SSH 配置。",
                    CliExitCodes.OperationError);
            }

            profile.Validate();
            var publisher = _remotePublisherFactory.Create(profile);
            return await ExecuteAsync(invocation, profile, publisher, stdout, stderr, cancellationToken);
        }
        catch (CliUsageException exception)
        {
            return WriteFailure(stdout, invocation?.Command ?? "help", invocation?.Json == true,
                CliExitCodes.UsageError, "USAGE_ERROR", exception.Message);
        }
        catch (CliOperationException exception)
        {
            return WriteFailure(stdout, invocation?.Command ?? "unknown", invocation?.Json == true,
                exception.ExitCode, exception.Code, exception.Message, exception.Details);
        }
        catch (OperationCanceledException)
        {
            return WriteFailure(stdout, invocation?.Command ?? "unknown", invocation?.Json == true,
                CliExitCodes.OperationError, "CANCELLED", "操作已取消。");
        }
        catch (Exception exception)
        {
            await stderr.WriteLineAsync(exception.ToString());
            return WriteFailure(stdout, invocation?.Command ?? "unknown", invocation?.Json == true,
                CliExitCodes.OperationError, "OPERATION_FAILED", exception.Message);
        }
    }

    private async Task<int> ExecuteAsync(
        CliInvocation invocation,
        ServerProfile profile,
        IRemotePublisher publisher,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken cancellationToken)
    {
        if (invocation.Command is not ("status" or "sync" or "list" or "publish" or "update" or "open" or "trash" or "restore" or "purge"))
        {
            throw new CliUsageException($"Unknown command: {invocation.Command}.");
        }

        if (invocation.Command == "status")
        {
            var status = await publisher.GetStatusAsync(cancellationToken);
            return WriteSuccess(stdout, invocation, new
            {
                serverTime = status.ServerTime,
                totalBytes = status.TotalBytes,
                freeBytes = status.FreeBytes,
                usedBytes = Math.Max(0, status.TotalBytes - status.FreeBytes)
            });
        }

        await using var context = await CliContext.CreateAsync(profile, publisher, _cacheFactory, cancellationToken);
        return invocation.Command switch
        {
            "sync" => await SyncAsync(invocation, context, stdout, cancellationToken),
            "list" => await ListAsync(invocation, context, stdout, cancellationToken),
            "publish" => await PublishAsync(invocation, context, stdout, stderr, cancellationToken),
            "update" => await UpdateAsync(invocation, context, stdout, stderr, cancellationToken),
            "open" => await OpenAsync(invocation, context, stdout, cancellationToken),
            "trash" => await TrashAsync(invocation, context, stdout, cancellationToken),
            "restore" => await RestoreAsync(invocation, context, stdout, cancellationToken),
            "purge" => await PurgeAsync(invocation, context, stdout, cancellationToken),
            _ => throw new InvalidOperationException("Unsupported command.")
        };
    }

    private static async Task<int> SyncAsync(
        CliInvocation invocation,
        CliContext context,
        TextWriter stdout,
        CancellationToken cancellationToken)
    {
        var sites = await context.RefreshAsync(cancellationToken);
        return WriteSuccess(stdout, invocation, new { sites = sites.Select(context.ToCliSite).ToArray(), count = sites.Count });
    }

    private static async Task<int> ListAsync(
        CliInvocation invocation,
        CliContext context,
        TextWriter stdout,
        CancellationToken cancellationToken)
    {
        var sites = await context.RefreshAsync(cancellationToken);
        var status = ParseStatus(invocation.Get("status"));
        var filtered = status is null ? sites : sites.Where(site => site.Status == status).ToArray();
        return WriteSuccess(stdout, invocation, new
        {
            status = status?.ToString().ToLowerInvariant() ?? "all",
            sites = filtered.Select(context.ToCliSite).ToArray(),
            count = filtered.Count
        });
    }

    private static async Task<int> PublishAsync(
        CliInvocation invocation,
        CliContext context,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken cancellationToken)
    {
        var source = invocation.GetRequired("source");
        var name = invocation.GetRequired("name").Trim();
        var note = invocation.Get("note") ?? string.Empty;
        var requestId = Guid.NewGuid();
        var archivePath = invocation.Get("archive") ?? new DefaultArchivePathFactory().CreatePath(requestId);
        var service = context.CreatePublishService();
        var site = await service.PublishAsync(
            new PublishSiteRequest(requestId, source, archivePath, name, note, ExistingSiteId: null),
            new Progress<PublishProgress>(progress => WriteProgress(stderr, progress)),
            cancellationToken);
        return WriteSuccess(stdout, invocation, new { site = context.ToCliSite(site), url = context.BuildUrl(site) });
    }

    private static async Task<int> UpdateAsync(
        CliInvocation invocation,
        CliContext context,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken cancellationToken)
    {
        var selected = await context.ResolveAsync(invocation.GetRequired("site"), cancellationToken);
        if (selected.Status != SiteStatus.Live)
        {
            throw new CliOperationException("SITE_NOT_LIVE", "只能更新已上架的网站。", CliExitCodes.SelectionError);
        }

        var source = invocation.GetRequired("source");
        var name = invocation.Get("name") ?? selected.Name;
        var note = invocation.Get("note") ?? selected.Note;
        var requestId = Guid.NewGuid();
        var archivePath = invocation.Get("archive") ?? new DefaultArchivePathFactory().CreatePath(requestId);
        var site = await context.CreatePublishService().PublishAsync(
            new PublishSiteRequest(requestId, source, archivePath, name.Trim(), note, selected.Id),
            new Progress<PublishProgress>(progress => WriteProgress(stderr, progress)),
            cancellationToken);
        return WriteSuccess(stdout, invocation, new { site = context.ToCliSite(site), url = context.BuildUrl(site) });
    }

    private static async Task<int> OpenAsync(
        CliInvocation invocation,
        CliContext context,
        TextWriter stdout,
        CancellationToken cancellationToken)
    {
        var site = await context.ResolveAsync(invocation.GetRequired("site"), cancellationToken);
        var url = context.BuildUrl(site);
        if (invocation.Launch)
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }

        return WriteSuccess(stdout, invocation, new { site = context.ToCliSite(site), url, launched = invocation.Launch });
    }

    private static Task<int> TrashAsync(CliInvocation invocation, CliContext context, TextWriter stdout, CancellationToken cancellationToken) =>
        MutateAsync(invocation, context, stdout, SiteStatus.Live, "SITE_NOT_LIVE", "trash", (id, token) => context.Publisher.TrashAsync(Guid.NewGuid(), id, token), cancellationToken);

    private static Task<int> RestoreAsync(CliInvocation invocation, CliContext context, TextWriter stdout, CancellationToken cancellationToken) =>
        MutateAsync(invocation, context, stdout, SiteStatus.Trash, "SITE_NOT_TRASHED", "restore", (id, token) => context.Publisher.RestoreAsync(Guid.NewGuid(), id, token), cancellationToken);

    private static async Task<int> PurgeAsync(
        CliInvocation invocation,
        CliContext context,
        TextWriter stdout,
        CancellationToken cancellationToken)
    {
        if (!invocation.Confirmed)
        {
            throw new CliOperationException("CONFIRMATION_REQUIRED", "永久删除必须显式提供 --yes。", CliExitCodes.SelectionError);
        }

        var selected = await context.ResolveAsync(invocation.GetRequired("site"), cancellationToken);
        if (selected.Status != SiteStatus.Trash)
        {
            throw new CliOperationException("SITE_NOT_TRASHED", "只有回收站中的网站可以永久删除。", CliExitCodes.SelectionError);
        }

        await context.Publisher.PurgeAsync(Guid.NewGuid(), selected.Id, cancellationToken);
        var sites = await context.RefreshAsync(cancellationToken);
        var remaining = sites.FirstOrDefault(site => site.Id == selected.Id);
        return WriteSuccess(stdout, invocation, new
        {
            siteId = selected.Id,
            purged = remaining is null || remaining.Status != SiteStatus.Trash
        });
    }

    private static async Task<int> MutateAsync(
        CliInvocation invocation,
        CliContext context,
        TextWriter stdout,
        SiteStatus requiredStatus,
        string invalidStatusCode,
        string command,
        Func<Guid, CancellationToken, Task<SiteManifest>> mutation,
        CancellationToken cancellationToken)
    {
        var selected = await context.ResolveAsync(invocation.GetRequired("site"), cancellationToken);
        if (selected.Status != requiredStatus)
        {
            throw new CliOperationException(invalidStatusCode,
                command == "trash" ? "只能将已上架的网站移入回收站。" : "只能恢复回收站中的网站。",
                CliExitCodes.SelectionError);
        }

        var updated = await mutation(selected.Id, cancellationToken);
        await context.RefreshAsync(cancellationToken);
        return WriteSuccess(stdout, invocation, new { site = context.ToCliSite(updated), url = context.BuildUrl(updated) });
    }

    private static SiteStatus? ParseStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (value.Equals("live", StringComparison.OrdinalIgnoreCase))
        {
            return SiteStatus.Live;
        }

        if (value.Equals("trash", StringComparison.OrdinalIgnoreCase))
        {
            return SiteStatus.Trash;
        }

        throw new CliUsageException("--status must be live, trash, or all.");
    }

    private static void WriteProgress(TextWriter stderr, PublishProgress progress)
    {
        var suffix = progress.TotalBytes > 0 ? $" {progress.CompletedBytes}/{progress.TotalBytes}" : string.Empty;
        stderr.WriteLine($"[{progress.Stage}]{suffix}");
    }

    private static int WriteSuccess(TextWriter stdout, CliInvocation invocation, object data)
    {
        if (invocation.Json)
        {
            stdout.WriteLine(JsonSerializer.Serialize(new CliResponse(true, invocation.Command, data), JsonOptions));
        }
        else
        {
            stdout.WriteLine(JsonSerializer.Serialize(data, JsonOptions));
        }

        return CliExitCodes.Success;
    }

    private static int WriteFailure(
        TextWriter stdout,
        string command,
        bool json,
        int exitCode,
        string errorCode,
        string message,
        object? details = null)
    {
        var response = new CliResponse(false, command, Error: new CliError(errorCode, message, details));
        if (json)
        {
            stdout.WriteLine(JsonSerializer.Serialize(response, JsonOptions));
        }
        else
        {
            stdout.WriteLine($"错误 [{errorCode}]：{message}");
        }

        return exitCode;
    }

    private static void WriteHelp(TextWriter stdout, bool json)
    {
        const string text = "用法：SiteManager.App.exe <status|sync|list|publish|update|open|trash|restore|purge> [选项]";
        if (json)
        {
            stdout.WriteLine(JsonSerializer.Serialize(new CliResponse(true, "help", new { usage = text }), JsonOptions));
        }
        else
        {
            stdout.WriteLine(text);
            stdout.WriteLine("通用选项：--json、--settings <path>、--help");
            stdout.WriteLine("站点选项：--site <id|slug|唯一名称>");
            stdout.WriteLine("发布选项：--source <folder> --name <name> [--note <note>] [--archive <path>]");
            stdout.WriteLine("永久删除：purge --site <selector> --yes");
        }
    }

    private static string GetVersion() =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "dev";

}

internal sealed class CliContext : IAsyncDisposable
{
    private readonly ISiteCache _cache;
    private readonly ServerProfile _profile;

    private CliContext(ServerProfile profile, IRemotePublisher publisher, ISiteCache cache)
    {
        _profile = profile;
        Publisher = publisher;
        _cache = cache;
    }

    public IRemotePublisher Publisher { get; }

    public static async Task<CliContext> CreateAsync(
        ServerProfile profile,
        IRemotePublisher publisher,
        Func<string, ISiteCache> cacheFactory,
        CancellationToken cancellationToken)
    {
        var cache = cacheFactory(GetDefaultCachePath());
        await cache.InitializeAsync(cancellationToken);
        return new CliContext(profile, publisher, cache);
    }

    public async Task<IReadOnlyList<SiteManifest>> RefreshAsync(CancellationToken cancellationToken)
    {
        var sites = await Publisher.ListAsync(status: null, cancellationToken);
        await _cache.ReplaceSitesAsync(sites.ToArray(), cancellationToken);
        return sites;
    }

    public async Task<SiteManifest> ResolveAsync(string selector, CancellationToken cancellationToken)
    {
        var sites = await RefreshAsync(cancellationToken);
        var matches = sites
            .Where(site => site.Id.ToString().Equals(selector, StringComparison.OrdinalIgnoreCase)
                || site.Slug.Equals(selector, StringComparison.OrdinalIgnoreCase)
                || site.Name.Equals(selector, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length == 1)
        {
            return matches[0];
        }

        if (matches.Length == 0)
        {
            throw new CliOperationException("SITE_NOT_FOUND", $"未找到站点：{selector}", CliExitCodes.SelectionError);
        }

        throw new CliOperationException(
            "SITE_SELECTOR_AMBIGUOUS",
            $"站点名称不唯一：{selector}。请改用 ID 或 slug。",
            CliExitCodes.SelectionError,
            new { candidates = matches.Select(site => new { site.Id, site.Name, site.Slug }).ToArray() });
    }

    public IPublishSiteService CreatePublishService() => new PublishSiteService(
        new WebsiteFolderValidator(),
        new TarGzipArchiveBuilder(),
        Publisher,
        _cache,
        new ResumableUploadEngine());

    public string BuildUrl(SiteManifest site) => site.BuildPublicUrl(_profile.PublicBaseUrl);

    public CliSite ToCliSite(SiteManifest site) => new(
        site.Id,
        site.Name,
        site.Note,
        site.Slug,
        site.Status,
        site.Version,
        site.SizeBytes,
        site.CreatedAt,
        site.UpdatedAt,
        site.TrashedAt,
        site.PurgeAt,
        BuildUrl(site));

    public async ValueTask DisposeAsync()
    {
        if (_cache is IAsyncDisposable disposable)
        {
            await disposable.DisposeAsync();
        }
    }

    private static string GetDefaultCachePath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SiteManager",
        "cache.db");
}
