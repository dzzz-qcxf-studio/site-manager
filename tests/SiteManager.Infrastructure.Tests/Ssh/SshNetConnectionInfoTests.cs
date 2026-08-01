using SiteManager.Infrastructure.Ssh;

namespace SiteManager.Infrastructure.Tests.Ssh;

public sealed class SshNetConnectionInfoTests
{
    [Fact]
    public void Host_key_fingerprint_requires_exact_openssh_sha256_value()
    {
        const string observed = "ZrZ2SF13RvyeSsLMuHl27GIelk8Yb09f1PBBae/1tbU";

        Assert.True(SshNetRemoteUploadStream.IsExpectedHostKeyFingerprint(
            observed,
            "SHA256:ZrZ2SF13RvyeSsLMuHl27GIelk8Yb09f1PBBae/1tbU"));
        Assert.False(SshNetRemoteUploadStream.IsExpectedHostKeyFingerprint(
            observed,
            "SHA256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"));
    }

    [Fact]
    public void Publish_command_quotes_empty_note_argument()
    {
        var command = SshNetRemotePublisher.BuildPublishCommand(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "测试",
            string.Empty);

        Assert.Contains("--name-b64 5rWL6K-V", command, StringComparison.Ordinal);
        Assert.Contains("--note-b64 \"\"", command, StringComparison.Ordinal);
    }
}
