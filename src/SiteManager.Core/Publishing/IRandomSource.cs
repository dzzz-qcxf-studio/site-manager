namespace SiteManager.Core.Publishing;

public interface IRandomSource
{
    int Next(int exclusiveMax);
}
