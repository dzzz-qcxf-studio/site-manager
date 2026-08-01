using SiteManager.Core.Remote;

namespace SiteManager.Core.Tests.Remote;

public sealed class RemoteProtocolTests
{
    [Fact]
    public void Parse_rejects_unknown_protocol_version()
    {
        const string response = """
            {"protocolVersion":2,"ok":true,"requestId":"0191f7d0-0000-7000-8000-000000000001","data":{"name":"ok"}}
            """;

        Assert.Throws<RemoteProtocolException>(() => RemoteProtocol.Parse<SampleData>(response));
    }

    [Fact]
    public void Parse_surfaces_retryable_remote_error()
    {
        const string response = """
            {"protocolVersion":1,"ok":false,"requestId":"0191f7d0-0000-7000-8000-000000000001","error":{"code":"HASH_MISMATCH","message":"bad hash","retryable":true}}
            """;

        var error = Assert.Throws<RemoteCommandException>(() => RemoteProtocol.Parse<SampleData>(response));

        Assert.Equal("HASH_MISMATCH", error.Code);
        Assert.True(error.Retryable);
        Assert.Equal(Guid.Parse("0191f7d0-0000-7000-8000-000000000001"), error.RequestId);
    }

    [Fact]
    public void EncodeText_uses_unpadded_base64url_and_round_trips_unicode()
    {
        const string source = "客户 A / 模型展示 ✓";

        var encoded = RemoteProtocol.EncodeText(source);

        Assert.DoesNotContain('=', encoded);
        Assert.DoesNotContain('+', encoded);
        Assert.DoesNotContain('/', encoded);
        Assert.Equal(source, RemoteProtocol.DecodeText(encoded));
    }

    private sealed record SampleData(string Name);
}
