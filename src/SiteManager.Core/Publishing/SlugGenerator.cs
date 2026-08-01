namespace SiteManager.Core.Publishing;

public sealed class SlugGenerator(IRandomSource random)
{
    private const string Alphabet = "abcdefghjkmnpqrstuvwxyz23456789";

    public string Generate(int length = 8)
    {
        if (length is < 6 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        var characters = new char[length];
        for (var index = 0; index < characters.Length; index++)
        {
            characters[index] = Alphabet[random.Next(Alphabet.Length)];
        }

        return new string(characters);
    }
}
