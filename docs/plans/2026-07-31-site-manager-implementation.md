# 网页展台管理器 Implementation Plan

> **For implementer:** Use TDD throughout. Write failing test first. Watch it fail. Then implement.

**Goal:** 构建一款 Windows WPF 软件，通过受限 SSH/SFTP 安全地发布、更新、查询、回收和恢复阿里云服务器上的静态网页文件夹。

**Architecture:** 客户端采用 .NET 8 WPF + MVVM，Core 层保存领域规则，Infrastructure 层实现压缩、SSH/SFTP、SQLite 和配置。服务器使用 Python 标准库实现 `site-managerctl`，以独立站点清单和原子符号链接管理版本，Nginx 只提供 `/s/` 静态内容。

**Tech Stack:** .NET 8、WPF、CommunityToolkit.Mvvm 8.4.2、SSH.NET 2025.1.0、Microsoft.Data.Sqlite 10.0.10、xUnit v3 3.2.2、Python 3、Nginx、systemd。

---

## 执行约束

1. 每项任务先写失败测试并实际看到预期失败，再写生产代码。
2. 每项任务完成后运行相关测试和完整测试套件。
3. 每次代码变化同步修改对应文档，并更新文档顶部日期。
4. NuGet 首先尝试国内镜像：`dotnet restore --source https://mirrors.cloud.tencent.com/nuget/v3/index.json`。镜像不可达或缺包时再使用 `https://api.nuget.org/v3/index.json`。
5. 私钥、口令、令牌、`.env` 和本地设置不得写入仓库。
6. 服务器变更前必须重新只读检查，并在修改 Nginx、root 公钥或永久删除前取得明确确认。

> 2026-08-01 执行顺序调整：用户需要尽早确认桌面视觉，因此在 Task 3 后提前执行 Task 12 的 WPF 应用壳与设计令牌。该调整只建立本地界面和导航，不提前接入 SSH，也不改变发布协议。

## Task 1: 建立解决方案与站点领域模型

**Files:**

- Create: `SiteManager.sln`
- Create: `Directory.Build.props`
- Create: `Directory.Packages.props`
- Create: `src/SiteManager.Core/SiteManager.Core.csproj`
- Create: `tests/SiteManager.Core.Tests/SiteManager.Core.Tests.csproj`
- Create: `tests/SiteManager.Core.Tests/Models/SiteManifestTests.cs`
- Create: `src/SiteManager.Core/Models/SiteManifest.cs`
- Modify: `docs/01-系统架构.md`

**Step 1: 创建空项目结构**

```powershell
dotnet new sln -n SiteManager
dotnet new classlib -n SiteManager.Core -o src/SiteManager.Core -f net8.0
dotnet new xunit -n SiteManager.Core.Tests -o tests/SiteManager.Core.Tests -f net8.0
dotnet sln add src/SiteManager.Core/SiteManager.Core.csproj tests/SiteManager.Core.Tests/SiteManager.Core.Tests.csproj
dotnet add tests/SiteManager.Core.Tests/SiteManager.Core.Tests.csproj reference src/SiteManager.Core/SiteManager.Core.csproj
```

中央包管理固定：`xunit.v3=3.2.2`、`Microsoft.NET.Test.Sdk=18.8.1`、`CommunityToolkit.Mvvm=8.4.2`、`SSH.NET=2025.1.0`、`Microsoft.Data.Sqlite=10.0.10`。

**Step 2: 写失败测试**

```csharp
using SiteManager.Core.Models;
namespace SiteManager.Core.Tests.Models;
public sealed class SiteManifestTests
{
    [Fact]
    public void BuildPublicUrl_uses_immutable_slug_and_trailing_slash()
    {
        var site = new SiteManifest(
            Guid.Parse("0191f7d0-0000-7000-8000-000000000100"),
            "产品模型演示", "客户 A", "a8k3m2", SiteStatus.Live,
            1, 10_485_760, new string('a', 64),
            DateTimeOffset.Parse("2026-07-31T12:00:00Z"),
            DateTimeOffset.Parse("2026-07-31T12:00:00Z"), null, null);
        Assert.Equal("http://47.86.89.203/s/a8k3m2/", site.BuildPublicUrl("http://47.86.89.203/s/"));
    }
}
```

**Step 3: 运行测试并确认失败**

Command: `dotnet test tests/SiteManager.Core.Tests/SiteManager.Core.Tests.csproj -- --filter-class SiteManager.Core.Tests.Models.SiteManifestTests`

Expected: FAIL，`SiteManifest` 或 `SiteStatus` 不存在。

**Step 4: 写最小实现**

```csharp
namespace SiteManager.Core.Models;
public enum SiteStatus { Live, Trash }
public sealed record SiteManifest(
    Guid Id, string Name, string Note, string Slug, SiteStatus Status,
    int Version, long SizeBytes, string ContentSha256,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    DateTimeOffset? TrashedAt, DateTimeOffset? PurgeAt)
{
    public string BuildPublicUrl(string baseUrl) => $"{baseUrl.TrimEnd('/')}/{Slug}/";
}
```

**Step 5: 验证、更新文档并提交**

```powershell
dotnet test
git add SiteManager.sln Directory.Build.props Directory.Packages.props src tests docs/01-系统架构.md
git commit -m "feat: establish site domain model"
```

Expected: 全部 PASS。

## Task 2: 网页文件夹安全校验

**Files:**

- Create: `src/SiteManager.Core/Validation/ValidationIssue.cs`
- Create: `src/SiteManager.Core/Validation/WebsiteFolderValidator.cs`
- Create: `tests/SiteManager.Core.Tests/Validation/WebsiteFolderValidatorTests.cs`
- Modify: `docs/02-Windows客户端.md`

**Step 1: 写失败测试**

```csharp
[Fact] public void Validate_rejects_folder_without_lowercase_index_html();
[Fact] public void Validate_rejects_private_key_and_dot_env();
[Fact] public void Validate_rejects_reparse_points();
[Fact] public void Validate_rejects_content_over_two_gibibytes();
[Fact] public void Validate_accepts_normal_static_site_and_returns_totals();
```

正常站点精确断言 `IsValid`、文件数和总字节数。

**Step 2: 运行并确认失败**

Command: `dotnet test tests/SiteManager.Core.Tests/SiteManager.Core.Tests.csproj -- --filter-class SiteManager.Core.Tests.Validation.WebsiteFolderValidatorTests`

Expected: FAIL，校验器不存在。

**Step 3: 写最小实现**

```csharp
public sealed record ValidationIssue(string Code, string RelativePath, string Message, bool IsError);
public sealed record FolderValidationResult(long TotalBytes, int FileCount, IReadOnlyList<ValidationIssue> Issues)
{
    public bool IsValid => Issues.All(x => !x.IsError);
}
```

`WebsiteFolderValidator.Validate` 固定执行：

```csharp
const long MaxBytes = 2L * 1024 * 1024 * 1024;
if (!File.Exists(Path.Combine(root, "index.html"))) Add("INDEX_MISSING", "index.html", true);
foreach (var path in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories))
{
    var relative = Path.GetRelativePath(root, path);
    var attributes = File.GetAttributes(path);
    if ((attributes & FileAttributes.ReparsePoint) != 0) Add("REPARSE_POINT", relative, true);
    if (Sensitive(relative)) Add("SENSITIVE_FILE", relative, true);
    if (File.Exists(path)) { total += new FileInfo(path).Length; count++; }
}
if (total > MaxBytes) Add("TOO_LARGE", ".", true);
```

敏感匹配至少包含 `.env`、`.env.*`、`.git/`、`.ssh/`、`id_rsa`、`id_ed25519`、`*.pem`、`*.key`；`node_modules` 只警告。

**Step 4: 验证并提交**

```powershell
dotnet test
git add src/SiteManager.Core/Validation tests/SiteManager.Core.Tests/Validation docs/02-Windows客户端.md
git commit -m "feat: validate static website folders"
```

## Task 3: 稳定短链接生成

**Files:**

- Create: `src/SiteManager.Core/Publishing/IRandomSource.cs`
- Create: `src/SiteManager.Core/Publishing/SlugGenerator.cs`
- Create: `tests/SiteManager.Core.Tests/Publishing/SlugGeneratorTests.cs`
- Modify: `docs/04-SSH发布协议.md`

**Step 1: 写失败测试**

```csharp
[Fact]
public void Generate_uses_lowercase_unambiguous_alphabet()
{
    var source = new SequenceRandomSource(0, 1, 2, 3, 4, 5, 6, 7);
    var slug = new SlugGenerator(source).Generate();
    Assert.Equal("abcdefgh", slug);
}
```

另写测试确认默认长度为 8，非法长度抛出 `ArgumentOutOfRangeException`。

**Step 2: 运行并确认失败**

Command: `dotnet test tests/SiteManager.Core.Tests/SiteManager.Core.Tests.csproj -- --filter-class SiteManager.Core.Tests.Publishing.SlugGeneratorTests`

Expected: FAIL，类型不存在。

**Step 3: 写最小实现**

```csharp
public interface IRandomSource { int Next(int exclusiveMax); }
public sealed class SlugGenerator(IRandomSource random)
{
    private const string Alphabet = "abcdefghjkmnpqrstuvwxyz23456789";
    public string Generate(int length = 8)
    {
        if (length is < 6 or > 12) throw new ArgumentOutOfRangeException(nameof(length));
        return string.Create(length, random, static (span, source) =>
        {
            for (var i = 0; i < span.Length; i++) span[i] = Alphabet[source.Next(Alphabet.Length)];
        });
    }
}
```

生产 `CryptoRandomSource` 使用 `RandomNumberGenerator.GetInt32`。

**Step 4: 验证并提交**

```powershell
dotnet test
git add src tests docs/04-SSH发布协议.md
git commit -m "feat: generate stable public slugs"
```

## Task 4: 流式归档与 SHA-256

**Files:**

- Create: `src/SiteManager.Core/Publishing/ArchiveResult.cs`
- Create: `src/SiteManager.Core/Publishing/IArchiveBuilder.cs`
- Create: `src/SiteManager.Infrastructure/SiteManager.Infrastructure.csproj`
- Create: `src/SiteManager.Infrastructure/Archives/TarGzipArchiveBuilder.cs`
- Create: `tests/SiteManager.Infrastructure.Tests/SiteManager.Infrastructure.Tests.csproj`
- Create: `tests/SiteManager.Infrastructure.Tests/Archives/TarGzipArchiveBuilderTests.cs`
- Modify: `docs/02-Windows客户端.md`

**Step 1: 写失败测试**

```csharp
[Fact]
public async Task BuildAsync_creates_relative_tar_entries_and_sha256()
{
    await using var fixture = await WebsiteFixture.CreateAsync(("index.html", "ok"), ("assets/a.js", "1"));
    var result = await new TarGzipArchiveBuilder().BuildAsync(fixture.Root, fixture.Output, null, default);
    Assert.Equal(64, result.Sha256.Length);
    Assert.True(result.CompressedBytes > 0);
    Assert.Equal(new[] { "assets/a.js", "index.html" }, await TarEntries.ReadNamesAsync(fixture.Output));
}
```

再写取消测试，断言 `OperationCanceledException` 且临时包被删除。

**Step 2: 运行并确认失败**

Command: `dotnet test tests/SiteManager.Infrastructure.Tests/SiteManager.Infrastructure.Tests.csproj -- --filter-class SiteManager.Infrastructure.Tests.Archives.TarGzipArchiveBuilderTests`

Expected: FAIL，归档实现不存在。

**Step 3: 写最小实现**

使用 `FileStream`、`GZipStream` 和 `System.Formats.Tar.TarWriter` 逐文件写入；entry 名称由 `Path.GetRelativePath` 生成并统一为 `/`。完成后用 `SHA256.HashDataAsync` 流式读取归档，返回：

```csharp
public sealed record ArchiveResult(string Path, long SourceBytes, long CompressedBytes, string Sha256);
```

任何异常都删除尚未完成的本地临时归档。

**Step 4: 验证并提交**

```powershell
dotnet test
git add SiteManager.sln src tests docs/02-Windows客户端.md
git commit -m "feat: build streaming website archives"
```

## Task 5: SSH JSON 协议与主机指纹配置

**Files:**

- Create: `src/SiteManager.Core/Remote/RemoteEnvelope.cs`
- Create: `src/SiteManager.Core/Remote/RemoteError.cs`
- Create: `src/SiteManager.Core/Remote/RemoteProtocol.cs`
- Create: `src/SiteManager.Core/Configuration/ServerProfile.cs`
- Create: `tests/SiteManager.Core.Tests/Remote/RemoteProtocolTests.cs`
- Create: `tests/SiteManager.Core.Tests/Configuration/ServerProfileTests.cs`
- Modify: `docs/04-SSH发布协议.md`
- Modify: `docs/08-配置与部署.md`

**Step 1: 写失败测试**

```csharp
[Fact] public void Parse_rejects_unknown_protocol_version();
[Fact] public void Parse_surfaces_retryable_remote_error();
[Fact] public void EncodeText_uses_unpadded_base64url_and_round_trips_unicode();
[Fact] public void ServerProfile_rejects_non_sha256_host_fingerprint();
```

**Step 2: 运行并确认失败**

Command: `dotnet test tests/SiteManager.Core.Tests/SiteManager.Core.Tests.csproj`

Expected: FAIL，协议类型不存在。

**Step 3: 写最小实现**

`RemoteProtocol.Parse<T>` 只接受 `protocolVersion == 1`；`ok == false` 时抛出包含 `Code`、`Retryable`、`RequestId` 的 `RemoteCommandException`。`EncodeText` 使用 UTF-8 Base64URL 并移除 `=`。

`ServerProfile` 验证端口 1–65535、安全用户名、非空私钥路径、OpenSSH SHA-256 指纹以及以 `/s/` 结尾的 HTTP/HTTPS 基础 URL。

**Step 4: 验证并提交**

```powershell
dotnet test
git add src tests docs/04-SSH发布协议.md docs/08-配置与部署.md
git commit -m "feat: define remote protocol and server profile"
```

## Task 6: 服务器清单、status 与 list

**Files:**

- Create: `server/site_manager/__init__.py`
- Create: `server/site_manager/models.py`
- Create: `server/site_manager/registry.py`
- Create: `server/site_manager/cli.py`
- Create: `server/tests/test_registry.py`
- Create: `server/tests/test_cli_status_list.py`
- Modify: `docs/03-服务器发布服务.md`
- Modify: `docs/04-SSH发布协议.md`

**Step 1: 写失败测试**

使用 `tempfile.TemporaryDirectory` 设置 `SITE_MANAGER_ROOT`，覆盖：

```python
def test_registry_writes_one_atomic_json_file_per_site(self): ...
def test_list_returns_live_and_trash_sorted_by_updated_at(self): ...
def test_status_returns_protocol_version_disk_and_server_time(self): ...
def test_invalid_manifest_does_not_hide_other_sites(self): ...
```

**Step 2: 运行并确认失败**

Command: `python -m unittest discover -s server/tests -v`

Expected: FAIL，`server.site_manager` 不存在。

**Step 3: 写最小实现**

- `models.py` 使用 `dataclasses.dataclass`，字段与 `docs/04` 完全一致。
- `Registry.save` 写到同目录临时文件，执行 `flush + os.fsync` 后 `os.replace`。
- `Registry.list` 独立读取每个 JSON，损坏项写 stderr，其他项继续返回。
- CLI stdout 只输出一行 JSON envelope，日志仅写 stderr。

**Step 4: 验证并提交**

```powershell
python -m unittest discover -s server/tests -v
dotnet test
git add server docs/03-服务器发布服务.md docs/04-SSH发布协议.md
git commit -m "feat: add server registry and status commands"
```

## Task 7: 服务端 prepare、publish 与原子更新

**Files:**

- Create: `server/site_manager/archive.py`
- Create: `server/site_manager/publisher.py`
- Create: `server/site_manager/locks.py`
- Create: `server/tests/test_archive_safety.py`
- Create: `server/tests/test_publish.py`
- Modify: `server/site_manager/cli.py`
- Modify: `docs/03-服务器发布服务.md`
- Modify: `docs/04-SSH发布协议.md`

**Step 1: 写失败测试**

```python
def test_prepare_is_idempotent_for_request_id(self): ...
def test_prepare_rejects_insufficient_space(self): ...
def test_publish_rejects_hash_mismatch_without_touching_live(self): ...
def test_publish_rejects_parent_path_and_symlink_tar_entries(self): ...
def test_publish_requires_root_index_html(self): ...
def test_update_switches_live_symlink_and_increments_version(self): ...
def test_failed_update_keeps_previous_live_target(self): ...
```

**Step 2: 运行并确认失败**

Command: `python -m unittest server.tests.test_archive_safety server.tests.test_publish -v`

Expected: FAIL，publisher 不存在。

**Step 3: 写最小实现**

- `prepare` 创建 `staging/<upload-id>/session.json`，保存 requestId、期望大小、哈希和过期时间。
- 空间判断使用 `shutil.disk_usage`，要求压缩大小 + 解压估算 + 512MiB。
- 解压前检查所有 tar 条目：禁止绝对路径、`..`、符号/硬链接、设备文件和目标目录逃逸。
- `publish` 验证大小与 SHA-256，再解压到 `versions/<site-id>/.vN.tmp`。
- 目录 `fsync` 后重命名为 `vN`；使用临时符号链接和 `os.replace` 原子切换 `live/<slug>`。
- 只有 live 切换成功后才写新清单；异常时恢复旧链接并删除临时版本。

**Step 4: 验证并提交**

```powershell
python -m unittest discover -s server/tests -v
git add server docs/03-服务器发布服务.md docs/04-SSH发布协议.md
git commit -m "feat: publish atomic website versions"
```

## Task 8: 服务端回收、恢复和 30 天清理

**Files:**

- Create: `server/site_manager/lifecycle.py`
- Create: `server/tests/test_lifecycle.py`
- Modify: `server/site_manager/cli.py`
- Modify: `docs/03-服务器发布服务.md`
- Modify: `docs/04-SSH发布协议.md`

**Step 1: 写失败测试**

```python
def test_trash_removes_live_and_sets_purge_at_30_days(self): ...
def test_restore_recreates_same_slug_and_clears_trash_dates(self): ...
def test_restore_rejects_slug_conflict(self): ...
def test_purge_expired_only_deletes_due_sites(self): ...
def test_trash_restore_and_purge_are_idempotent(self): ...
```

向服务注入 `clock`，固定时间为 `2026-07-31T12:00:00Z`，断言 `purgeAt` 为 `2026-08-30T12:00:00Z`。

**Step 2: 运行并确认失败**

Command: `python -m unittest server.tests.test_lifecycle -v`

Expected: FAIL，生命周期实现不存在。

**Step 3: 写最小实现**

所有操作获取站点锁。`trash` 先移除 live 链接，再移动版本到 `trash/<site-id>`；`restore` 在 slug 空闲时逆操作；`purge` 只接受 trash 站点。清单写入和目录移动失败时执行补偿恢复。

**Step 4: 验证并提交**

```powershell
python -m unittest discover -s server/tests -v
git add server docs/03-服务器发布服务.md docs/04-SSH发布协议.md
git commit -m "feat: add recoverable site lifecycle"
```

## Task 9: 可续传 SFTP 上传引擎

**Files:**

- Create: `src/SiteManager.Core/Transfers/UploadProgress.cs`
- Create: `src/SiteManager.Core/Transfers/IRemoteUploadStream.cs`
- Create: `src/SiteManager.Core/Transfers/ResumableUploadEngine.cs`
- Create: `tests/SiteManager.Core.Tests/Transfers/ResumableUploadEngineTests.cs`
- Create: `src/SiteManager.Infrastructure/Ssh/SshNetRemoteUploadStream.cs`
- Create: `tests/SiteManager.Infrastructure.Tests/Ssh/SshNetConnectionInfoTests.cs`
- Modify: `docs/02-Windows客户端.md`

**Step 1: 写失败测试**

```csharp
[Fact] public async Task UploadAsync_starts_at_remote_offset();
[Fact] public async Task UploadAsync_reports_monotonic_byte_progress();
[Fact] public async Task UploadAsync_rejects_remote_offset_larger_than_source();
[Fact] public async Task UploadAsync_stops_on_cancellation_without_truncating_remote_partial();
```

测试使用内存 `FakeRemoteUploadStream`：远端已有前 4 字节，源为 10 字节，最终只传剩余 6 字节。

**Step 2: 运行并确认失败**

Command: `dotnet test tests/SiteManager.Core.Tests/SiteManager.Core.Tests.csproj -- --filter-class SiteManager.Core.Tests.Transfers.ResumableUploadEngineTests`

Expected: FAIL，上传引擎不存在。

**Step 3: 写最小实现**

```csharp
public async Task UploadAsync(Stream source, IRemoteUploadStream remote,
    IProgress<UploadProgress>? progress, CancellationToken cancellationToken)
{
    var offset = await remote.GetLengthAsync(cancellationToken);
    if (offset > source.Length) throw new InvalidDataException("Remote partial exceeds source length.");
    source.Position = offset;
    await remote.SeekAsync(offset, cancellationToken);
    var buffer = new byte[1024 * 1024];
    while (true)
    {
        var read = await source.ReadAsync(buffer, cancellationToken);
        if (read == 0) break;
        await remote.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        offset += read;
        progress?.Report(new UploadProgress(offset, source.Length));
    }
    await remote.FlushAsync(cancellationToken);
}
```

SSH.NET 适配器必须校验收到的 host key SHA-256 与 `ServerProfile` 完全相同，不匹配立即中断。

**Step 4: 验证并提交**

```powershell
dotnet test
git add src tests docs/02-Windows客户端.md
git commit -m "feat: upload archives with resumable sftp"
```

## Task 10: 本地 SQLite 缓存与任务恢复

**Files:**

- Create: `src/SiteManager.Core/Storage/ISiteCache.cs`
- Create: `src/SiteManager.Core/Transfers/TransferCheckpoint.cs`
- Create: `src/SiteManager.Infrastructure/Storage/SqliteSiteCache.cs`
- Create: `tests/SiteManager.Infrastructure.Tests/Storage/SqliteSiteCacheTests.cs`
- Modify: `docs/02-Windows客户端.md`

**Step 1: 写失败测试**

```csharp
[Fact] public async Task ReplaceSitesAsync_replaces_cache_in_one_transaction();
[Fact] public async Task SaveCheckpointAsync_round_trips_upload_id_offset_and_archive_path();
[Fact] public async Task DeleteCheckpointAsync_is_idempotent();
[Fact] public async Task InitializeAsync_migrates_schema_version_one();
```

每个测试使用独立临时 `.db` 文件。

**Step 2: 运行并确认失败**

Command: `dotnet test tests/SiteManager.Infrastructure.Tests/SiteManager.Infrastructure.Tests.csproj -- --filter-class SiteManager.Infrastructure.Tests.Storage.SqliteSiteCacheTests`

Expected: FAIL，缓存实现不存在。

**Step 3: 写最小实现**

```sql
CREATE TABLE sites (id TEXT PRIMARY KEY, json TEXT NOT NULL, updated_at TEXT NOT NULL);
CREATE TABLE transfer_checkpoints (
  request_id TEXT PRIMARY KEY, upload_id TEXT NOT NULL, site_id TEXT NULL,
  archive_path TEXT NOT NULL, remote_path TEXT NOT NULL,
  expected_sha256 TEXT NOT NULL, total_bytes INTEGER NOT NULL, updated_at TEXT NOT NULL
);
CREATE TABLE schema_info (version INTEGER NOT NULL);
```

使用参数化 SQL 和事务；SQLite 失败不能删除服务器数据。

**Step 4: 验证并提交**

```powershell
dotnet test
git add src tests docs/02-Windows客户端.md
git commit -m "feat: cache sites and transfer checkpoints"
```

## Task 11: 发布、更新和同步用例编排

> 已于 2026-08-01 完成；Core 用例、调用顺序测试和全量 .NET 测试均已验证。

**Files:**

- Create: `src/SiteManager.Core/Publishing/IRemotePublisher.cs`
- Create: `src/SiteManager.Core/Publishing/PublishSiteRequest.cs`
- Create: `src/SiteManager.Core/Publishing/PublishSiteService.cs`
- Create: `src/SiteManager.Core/Publishing/SiteSyncService.cs`
- Create: `tests/SiteManager.Core.Tests/Publishing/PublishSiteServiceTests.cs`
- Create: `tests/SiteManager.Core.Tests/Publishing/SiteSyncServiceTests.cs`
- Modify: `docs/01-系统架构.md`
- Modify: `docs/02-Windows客户端.md`

**Step 1: 写失败测试**

```csharp
[Fact] public async Task PublishAsync_validates_before_archive_or_network();
[Fact] public async Task PublishAsync_saves_checkpoint_before_upload();
[Fact] public async Task PublishAsync_deletes_checkpoint_only_after_remote_publish_success();
[Fact] public async Task UpdateAsync_passes_existing_site_id_and_keeps_slug();
[Fact] public async Task SyncAsync_replaces_local_cache_with_server_list();
```

使用可记录调用顺序的 fake，断言 validate → archive → prepare → checkpoint → upload → publish → cache → checkpoint delete。

**Step 2: 运行并确认失败**

Command: `dotnet test tests/SiteManager.Core.Tests/SiteManager.Core.Tests.csproj`

Expected: FAIL，用例服务不存在。

**Step 3: 写最小实现**

`PublishSiteService` 只编排接口，不包含 UI、SSH.NET 或 SQLite 细节。进度阶段固定为 `Scanning`、`Archiving`、`Preparing`、`Uploading`、`Verifying`、`Publishing`、`Completed`、`Failed`、`Cancelled`。

**Step 4: 验证并提交**

```powershell
dotnet test
git add src tests docs/01-系统架构.md docs/02-Windows客户端.md
git commit -m "feat: orchestrate site publishing workflows"
```

## Task 12: WPF 应用壳与设计令牌

> 已于 2026-08-01 提前完成；运行预览见 `docs/assets/site-manager-wpf-preview-v0.png`。

**Files:**

- Create: `src/SiteManager.App/SiteManager.App.csproj`
- Create: `src/SiteManager.App/App.xaml`
- Create: `src/SiteManager.App/App.xaml.cs`
- Create: `src/SiteManager.App/Resources/Colors.xaml`
- Create: `src/SiteManager.App/Resources/Typography.xaml`
- Create: `src/SiteManager.App/Resources/Controls.xaml`
- Create: `src/SiteManager.App/Views/MainWindow.xaml`
- Create: `src/SiteManager.App/ViewModels/ShellViewModel.cs`
- Create: `tests/SiteManager.App.Tests/SiteManager.App.Tests.csproj`
- Create: `tests/SiteManager.App.Tests/ViewModels/ShellViewModelTests.cs`
- Modify: `docs/02-Windows客户端.md`

**Step 1: 写失败测试**

```csharp
[Fact]
public void Default_section_is_live_sites()
{
    var vm = new ShellViewModel();
    Assert.Equal(AppSection.LiveSites, vm.CurrentSection);
}
[Fact] public void Navigate_changes_section_and_selection_state() { ... }
```

**Step 2: 运行并确认失败**

Command: `dotnet test tests/SiteManager.App.Tests/SiteManager.App.Tests.csproj -- --filter-class SiteManager.App.Tests.ViewModels.ShellViewModelTests`

Expected: FAIL，WPF App 或 ViewModel 不存在。

**Step 3: 写最小实现**

- `ShellViewModel` 使用 CommunityToolkit.Mvvm 的 `ObservableObject` 和 `RelayCommand<AppSection>`。
- `Colors.xaml` 只定义已批准颜色，不引入未记录色相。
- `Controls.xaml` 定义 2px 描边、硬阴影、胶囊按钮、焦点、禁用和加载状态。
- MainWindow 左侧导航、右侧内容；默认 1180×760，最小 1024×640。

**Step 4: 验证并提交**

```powershell
dotnet test
dotnet build src/SiteManager.App/SiteManager.App.csproj -c Debug
git add SiteManager.sln src tests docs/02-Windows客户端.md
git commit -m "feat: create styled wpf application shell"
```

## Task 13: 已上架、发布、传输和回收站界面

> 已于 2026-08-01 完成；业务页面、空态、失败状态、发布取消和永久删除确认均已实现并通过自动化测试与运行时预览验证。

**Files:**

- Create: `src/SiteManager.App/ViewModels/LiveSitesViewModel.cs`
- Create: `src/SiteManager.App/ViewModels/PublishViewModel.cs`
- Create: `src/SiteManager.App/ViewModels/TransferCenterViewModel.cs`
- Create: `src/SiteManager.App/ViewModels/TrashViewModel.cs`
- Create: `src/SiteManager.App/Views/LiveSitesView.xaml`
- Create: `src/SiteManager.App/Views/PublishView.xaml`
- Create: `src/SiteManager.App/Views/TransferCenterView.xaml`
- Create: `src/SiteManager.App/Views/TrashView.xaml`
- Create: `tests/SiteManager.App.Tests/ViewModels/LiveSitesViewModelTests.cs`
- Create: `tests/SiteManager.App.Tests/ViewModels/PublishViewModelTests.cs`
- Create: `tests/SiteManager.App.Tests/ViewModels/TrashViewModelTests.cs`
- Modify: `docs/02-Windows客户端.md`

**Step 1: 写失败测试**

```csharp
[Fact] public void Search_filters_name_note_and_url_case_insensitively();
[Fact] public void Publish_is_disabled_until_folder_is_valid_and_name_present();
[Fact] public async Task Publish_command_exposes_stage_and_cancellation();
[Fact] public async Task Copy_link_uses_selected_site_url();
[Fact] public async Task Restore_refreshes_live_and_trash_lists();
[Fact] public void Purge_command_requires_explicit_confirmation_service();
```

**Step 2: 运行并确认失败**

Command: `dotnet test tests/SiteManager.App.Tests/SiteManager.App.Tests.csproj`

Expected: FAIL，ViewModel 不存在。

**Step 3: 写最小实现**

- ViewModel 只调用 Core 用例和抽象的对话框、剪贴板、浏览器服务。
- 列表覆盖加载、空、离线、错误和正常状态。
- 发布页展示扫描结果、风险项、大小和分阶段进度。
- 回收站展示 `purgeAt` 本地时间和预计释放空间。
- XAML 使用批准令牌；主操作为珊瑚红，状态辅色只表达语义。

**Step 4: 视觉与自动验证**

```powershell
dotnet test
dotnet build -c Release
```

手工检查 1280×800、1440×900、1920×1080：无文本溢出、焦点可见，按钮具备 hover/active/disabled/loading，列表和空状态符合参考风格。

**Step 5: 提交**

```powershell
git add src tests docs/02-Windows客户端.md
git commit -m "feat: add complete site management views"
```

## Task 14: 服务器安装、Nginx 和定时清理

> 已于 2026-08-01 完成；本地部署资产、默认 dry-run 行为、Nginx 静态片段与每日清理 timer 均有自动化验证。尚未将任何资产应用到生产服务器。

**Files:**

- Create: `deploy/install-server.sh`
- Create: `deploy/nginx/site-manager-location.conf`
- Create: `deploy/systemd/site-manager-purge.service`
- Create: `deploy/systemd/site-manager-purge.timer`
- Create: `server/tests/test_deploy_assets.py`
- Modify: `docs/03-服务器发布服务.md`
- Modify: `docs/08-配置与部署.md`

**Step 1: 写失败测试**

```python
def test_nginx_location_is_static_and_blocks_dotfiles(self): ...
def test_nginx_declares_glb_gltf_wasm_mime_types(self): ...
def test_timer_runs_daily_and_is_persistent(self): ...
def test_installer_has_dry_run_and_never_deletes_existing_web_root(self): ...
```

**Step 2: 运行并确认失败**

Command: `python -m unittest server.tests.test_deploy_assets -v`

Expected: FAIL，部署资源不存在。

**Step 3: 写最小实现**

- 安装脚本支持 `--dry-run`，默认不修改。
- 创建用户、目录和权限时保持幂等。
- Nginx 使用独立 snippet，只挂载 `/s/`，不修改现有 root。
- timer 每日执行 `site-managerctl purge-expired`。
- 目标文件先写临时文件再替换，Nginx 重载前执行 `nginx -t`。

**Step 4: 本地验证并提交**

```powershell
python -m unittest discover -s server/tests -v
git add deploy server/tests docs/03-服务器发布服务.md docs/08-配置与部署.md
git commit -m "feat: add safe server provisioning assets"
```

## Task 15: 设置页、依赖注入与真实 SSH 适配

> 完成于 2026-08-01：已实现 JSON 设置存储、设置页、严格主机指纹校验的 SSH/SFTP 适配与应用启动组合；自动化测试覆盖私钥只存路径、设置校验和只读 `status` 连通性测试。

**Files:**

- Create: `src/SiteManager.Infrastructure/Ssh/SshNetRemotePublisher.cs`
- Create: `src/SiteManager.Infrastructure/Configuration/JsonSettingsStore.cs`
- Create: `src/SiteManager.App/ViewModels/SettingsViewModel.cs`
- Create: `src/SiteManager.App/Views/SettingsView.xaml`
- Create: `tests/SiteManager.Infrastructure.Tests/Configuration/JsonSettingsStoreTests.cs`
- Create: `tests/SiteManager.App.Tests/ViewModels/SettingsViewModelTests.cs`
- Modify: `src/SiteManager.App/App.xaml.cs`
- Modify: `docs/02-Windows客户端.md`
- Modify: `docs/08-配置与部署.md`

**Step 1: 写失败测试**

```csharp
[Fact] public async Task SettingsStore_never_serializes_private_key_contents();
[Fact] public async Task TestConnection_calls_status_only();
[Fact] public async Task Host_key_mismatch_returns_security_error();
[Fact] public void Save_is_disabled_for_invalid_profile();
```

**Step 2: 运行并确认失败**

Command: `dotnet test`

Expected: FAIL，实现不存在。

**Step 3: 写最小实现**

- 设置保存到 `%APPDATA%/SiteManager/settings.json`，只保存私钥路径。
- `SshNetRemotePublisher` 只映射 `docs/04` 固定命令；参数使用白名单、UUID、数字、哈希和 Base64URL。
- 依赖注入在 `App.xaml.cs` 组合，不在 ViewModel 中 `new` 基础设施。
- 测试连接只执行 `status`，不写远端状态。

**Step 4: 验证并提交**

```powershell
dotnet test
dotnet build -c Release
git add src tests docs/02-Windows客户端.md docs/08-配置与部署.md
git commit -m "feat: connect desktop app to constrained ssh backend"
```

## Task 16: 测试服务器部署与端到端验收

> 完成于 2026-08-01：完成服务器 Gate、dry-run、隔离安装和独立 80 端口 Nginx 服务；Windows 端到端脚本通过，服务器 Linux 25 项测试通过，测试站点已永久清理。root 既有公钥未删除。

**Files:**

- Create: `tests/e2e/TestSite/index.html`
- Create: `tests/e2e/TestSite/assets/version.txt`
- Create: `scripts/e2e-smoke.ps1`
- Modify: `docs/08-配置与部署.md`

**Step 1: 写端到端验收脚本并先运行**

脚本依次断言：status 可用；发布后 HTTP 返回唯一标记；更新后 URL 不变且内容改变；回收后 404；恢复后同 URL 返回 200；清理后无测试残留。

首次运行 Expected: FAIL，因为服务器尚未安装 `site-managerctl`。

**Step 2: 修改服务器前进行 Gate 和备份**

```powershell
ssh -i "$env:USERPROFILE\.ssh\site_manager_ed25519" root@47.86.89.203 "ss -lntp; nginx -T; df -h /; getent passwd sitepublisher || true"
```

确认现有服务后展示 dry-run，取得用户确认再安装；备份 Nginx 配置到带 UTC 时间戳的文件。

**Step 3: 安装并运行端到端测试**

Command: `./scripts/e2e-smoke.ps1`

Expected: 所有阶段 PASS，现有根网站仍返回原内容。

**Step 4: 收紧密钥权限**

确认 `sitepublisher` 登录和用户原有 root 公钥有效后，再取得明确确认，从 root `authorized_keys` 精确删除注释为 `site-manager@47.86.89.203` 的专用公钥，不修改其他行。

**Step 5: 更新文档并提交**

```powershell
git add tests/e2e scripts/e2e-smoke.ps1 docs/08-配置与部署.md
git commit -m "test: verify server publishing end to end"
```

## Task 17: 发布 Windows x64 软件包

> 完成于 2026-08-01：Release 测试通过并生成自包含 Windows x64 ZIP；包内含可执行程序、README 和非敏感设置模板，ZIP SHA-256 已写入同目录校验文件，启动烟雾检查通过。随后修复了已保存配置启动时 UI 线程同步等待导致的死锁、进度条对只读进度属性的错误双向绑定、空备注 SSH 参数缺失、发布错误横幅绑定、主窗口服务器状态卡片硬编码占位文案、剪贴板被占用时复制链接导致的崩溃、启动先显示本地站点缓存并在窗口显示后后台自动同步、新建网站入口、上架后自动进入传输中心、传输历史、更新目录恢复、更新前保存目录以及移除发布页次级“新建网站”按钮；重新生成 `SiteManager-win-x64-fixed.zip`（SHA-256：`69f4305ad0f357fe048ba74af0fb5fb6329f8ef87046083d03cdcf62174b71e1`）。

**Files:**

- Create: `scripts/publish-win-x64.ps1`
- Create: `README.md`
- Modify: `src/SiteManager.App/SiteManager.App.csproj`
- Modify: `docs/00-索引.md`
- Modify: `docs/02-Windows客户端.md`
- Modify: `docs/08-配置与部署.md`

**Step 1: 写发布脚本验收并先运行**

脚本先执行全部测试；任一失败必须非零退出，不产生成功标记。缺少 `SiteManager.App.exe`、文档或默认非敏感配置模板时失败。

首次运行 Expected: FAIL，因为发布属性尚未配置。

**Step 2: 配置自包含发布**

```xml
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
<SelfContained>true</SelfContained>
<PublishSingleFile>true</PublishSingleFile>
<IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
<PublishTrimmed>false</PublishTrimmed>
```

```powershell
dotnet test -c Release
dotnet publish src/SiteManager.App/SiteManager.App.csproj -c Release -r win-x64 --self-contained true -o artifacts/win-x64
Compress-Archive -Path artifacts/win-x64/* -DestinationPath artifacts/SiteManager-win-x64.zip -Force
```

**Step 3: 验证软件包**

- 在未安装开发 SDK 的 Windows 环境启动。
- 首次启动只选择私钥路径，不复制私钥。
- 连接、同步、发布、更新、回收和恢复均通过。
- Windows Defender 扫描无告警；若未签名，README 说明来源和 SHA-256。

**Step 4: 更新全部文档与最终验证**

```powershell
dotnet test -c Release
python -m unittest discover -s server/tests -v
git diff --check
git status --short
```

确认所有文档描述与代码一致，更新对应“最后更新”日期。

**Step 5: 提交**

```powershell
git add README.md scripts src docs
git commit -m "build: package site manager for windows x64"
```

## 完成定义

- .NET 与 Python 全部测试通过。
- WPF Release 构建和 Windows x64 自包含发布成功。
- 服务器端到端测试通过且现有网站未受影响。
- 更新不改 URL，失败不影响旧版本。
- 回收站 30 天规则、恢复和永久删除均验证。
- 2GB 路径全程流式处理，无整体内存加载。
- 仓库和安装包不包含私钥、口令、API Key 或本地设置。
- `docs/00`、`01`、`02`、`03`、`04`、`08` 与实际实现一致。
