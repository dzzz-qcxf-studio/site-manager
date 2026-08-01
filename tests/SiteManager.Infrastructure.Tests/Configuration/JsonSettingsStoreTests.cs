using SiteManager.Core.Configuration;
using SiteManager.Infrastructure.Configuration;

namespace SiteManager.Infrastructure.Tests.Configuration;

public sealed class JsonSettingsStoreTests
{
    [Fact]
    public async Task SettingsStore_never_serializes_private_key_contents()
    {
        using var fixture = TemporaryDirectory.Create();
        var privateKeyPath = Path.Combine(fixture.Path, "site_manager_ed25519");
        const string privateKeyContents = "TEST-PRIVATE-KEY-CONTENTS\nsecret\nEND-TEST-PRIVATE-KEY";
        await File.WriteAllTextAsync(privateKeyPath, privateKeyContents, TestContext.Current.CancellationToken);
        var settingsPath = Path.Combine(fixture.Path, "settings.json");
        var profile = CreateProfile(privateKeyPath);
        var store = new JsonSettingsStore(settingsPath);

        await store.SaveAsync(profile, TestContext.Current.CancellationToken);

        var serialized = await File.ReadAllTextAsync(settingsPath, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(privateKeyContents, serialized, StringComparison.Ordinal);
        Assert.Contains("site_manager_ed25519", serialized, StringComparison.Ordinal);
        Assert.Equal(profile, await store.LoadAsync(TestContext.Current.CancellationToken));
    }

    private static ServerProfile CreateProfile(string privateKeyPath) => new(
        "47.86.89.203", 22, "sitepublisher", privateKeyPath,
        "SHA256:ZrZ2SF13RvyeSsLMuHl27GIelk8Yb09f1PBBae/1tbU", "http://47.86.89.203/s/");

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path) => Path = path;

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"site-manager-settings-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
