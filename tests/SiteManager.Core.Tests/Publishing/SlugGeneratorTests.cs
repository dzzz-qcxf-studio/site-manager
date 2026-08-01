using SiteManager.Core.Publishing;

namespace SiteManager.Core.Tests.Publishing;

public sealed class SlugGeneratorTests
{
    [Fact]
    public void Generate_uses_values_from_random_source_in_order()
    {
        var source = new SequenceRandomSource(0, 1, 2, 3, 4, 5, 6, 7);

        var slug = new SlugGenerator(source).Generate();

        Assert.Equal("abcdefgh", slug);
    }

    [Fact]
    public void Generate_defaults_to_eight_characters()
    {
        var source = new SequenceRandomSource(0, 0, 0, 0, 0, 0, 0, 0);

        var slug = new SlugGenerator(source).Generate();

        Assert.Equal(8, slug.Length);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(12)]
    public void Generate_accepts_supported_length_boundaries(int length)
    {
        var source = new SequenceRandomSource(Enumerable.Repeat(0, length).ToArray());

        var slug = new SlugGenerator(source).Generate(length);

        Assert.Equal(length, slug.Length);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(13)]
    public void Generate_rejects_lengths_outside_supported_range(int length)
    {
        var source = new SequenceRandomSource();

        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new SlugGenerator(source).Generate(length));

        Assert.Equal("length", exception.ParamName);
    }

    [Fact]
    public void Generate_uses_lowercase_unambiguous_alphabet()
    {
        var source = new SequenceRandomSource(Enumerable.Range(0, 31).ToArray());
        var generator = new SlugGenerator(source);

        var alphabet = generator.Generate(12) + generator.Generate(12) + generator.Generate(7);

        Assert.Equal("abcdefghjkmnpqrstuvwxyz23456789", alphabet);
        Assert.DoesNotContain(alphabet, character => "ilo01".Contains(character));
    }

    private sealed class SequenceRandomSource(params int[] values) : IRandomSource
    {
        private readonly Queue<int> _values = new(values);

        public int Next(int exclusiveMax)
        {
            var value = _values.Dequeue();
            Assert.InRange(value, 0, exclusiveMax - 1);
            return value;
        }
    }
}
