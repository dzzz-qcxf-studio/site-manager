using SiteManager.App.CommandLine;

namespace SiteManager.App.Tests.CommandLine;

public sealed class CommandLineParserTests
{
    [Fact]
    public void Parses_json_publish_options()
    {
        var invocation = CommandLineParser.Parse([
            "publish",
            "--json",
            "--source", "D:\\sites\\demo",
            "--name", "客户展示",
            "--note", "第一版"
        ]);

        Assert.Equal("publish", invocation.Command);
        Assert.True(invocation.Json);
        Assert.Equal("D:\\sites\\demo", invocation.GetRequired("source"));
        Assert.Equal("客户展示", invocation.GetRequired("name"));
        Assert.Equal("第一版", invocation.GetRequired("note"));
    }

    [Fact]
    public void Parses_boolean_confirmation_and_launch_flags()
    {
        var invocation = CommandLineParser.Parse(["purge", "--site", "alpha-one", "--yes"]);

        Assert.Equal("purge", invocation.Command);
        Assert.True(invocation.Confirmed);
        Assert.Equal("alpha-one", invocation.GetRequired("site"));
    }

    [Fact]
    public void Rejects_unknown_options()
    {
        var exception = Assert.Throws<CliUsageException>(() => CommandLineParser.Parse(["list", "--unknown"]));

        Assert.Contains("unknown", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_missing_option_value()
    {
        var exception = Assert.Throws<CliUsageException>(() => CommandLineParser.Parse(["open", "--site"]));

        Assert.Contains("site", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
