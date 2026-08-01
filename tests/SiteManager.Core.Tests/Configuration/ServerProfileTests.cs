using SiteManager.Core.Configuration;

namespace SiteManager.Core.Tests.Configuration;

public sealed class ServerProfileTests
{
    [Fact]
    public void Validate_rejects_non_sha256_host_fingerprint()
    {
        var profile = CreateProfile() with { HostKeySha256 = "SHA1:bad" };

        var error = Assert.Throws<ArgumentException>(profile.Validate);

        Assert.Contains("fingerprint", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_accepts_approved_profile_shape()
    {
        var profile = CreateProfile();

        profile.Validate();
    }

    [Fact]
    public void Validate_requires_public_base_url_to_end_in_s_segment()
    {
        var profile = CreateProfile() with { PublicBaseUrl = "http://47.86.89.203/sites/" };

        Assert.Throws<ArgumentException>(profile.Validate);
    }

    private static ServerProfile CreateProfile() => new(
        "47.86.89.203",
        22,
        "sitepublisher",
        @"C:\Users\ROG\.ssh\site_manager_ed25519",
        "SHA256:ZrZ2SF13RvyeSsLMuHl27GIelk8Yb09f1PBBae/1tbU",
        "http://47.86.89.203/s/",
        30);
}
