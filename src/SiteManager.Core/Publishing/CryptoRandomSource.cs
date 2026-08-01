using System.Security.Cryptography;

namespace SiteManager.Core.Publishing;

public sealed class CryptoRandomSource : IRandomSource
{
    public int Next(int exclusiveMax) => RandomNumberGenerator.GetInt32(exclusiveMax);
}
