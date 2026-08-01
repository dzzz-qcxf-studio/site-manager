using SiteManager.Core.Models;

namespace SiteManager.Core.Tests.Models;

public sealed class SiteManifestTests
{
    [Fact]
    public void BuildPublicUrl_uses_immutable_slug_and_trailing_slash()
    {
        var site = new SiteManifest(
            Guid.Parse("0191f7d0-0000-7000-8000-000000000100"),
            "产品模型演示",
            "客户 A",
            "a8k3m2",
            SiteStatus.Live,
            1,
            10_485_760,
            new string('a', 64),
            DateTimeOffset.Parse("2026-07-31T12:00:00Z"),
            DateTimeOffset.Parse("2026-07-31T12:00:00Z"),
            null,
            null);

        Assert.Equal(
            "http://47.86.89.203/s/a8k3m2/",
            site.BuildPublicUrl("http://47.86.89.203/s/"));
    }
}
