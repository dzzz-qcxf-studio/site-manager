using Xunit;

namespace SiteManager.App.Tests.Resources;

public sealed class TypographyResourceTests
{
    [Fact]
    public void Typography_dictionary_keeps_display_font_alias_for_existing_views()
    {
        var repositoryRoot = FindRepositoryRoot();
        var dictionaryPath = Path.Combine(repositoryRoot, "src", "SiteManager.App", "Resources", "Typography.xaml");
        var contents = File.ReadAllText(dictionaryPath);

        Assert.Contains("x:Key=\"DisplayFont\"", contents, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SiteManager.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
