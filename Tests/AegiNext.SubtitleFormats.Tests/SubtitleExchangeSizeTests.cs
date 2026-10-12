using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.SubtitleFormats;

namespace AegiNext.SubtitleFormats.Tests;

public sealed class SubtitleExchangeSizeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SubtitleFormatsRoundTripMoreThanSixteenMiCharacters(bool ass)
    {
        var content = new string(ass ? '{' : 'x', 65536);
        var lines = Enumerable.Range(0, ass ? 128 : 257).Select(index => new SubtitleLine
        {
            Start = new(index), End = new(index + 1), Text = content
        }).ToImmutableArray();
        var document = new ProjectDocument
        {
            Subtitles = lines,
            Layers = lines.Select(line => new ProjectLayer
            {
                Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End
            }).ToImmutableArray()
        };

        var source = ass ? AssSubtitleFormat.Write(document).Text : SubtitleTextFormat.WriteSrt(lines);
        Assert.True(source.Length > 16 * 1024 * 1024);
        var imported = ass ? AssSubtitleFormat.Parse(source).Lines : SubtitleTextFormat.ParseSrt(source);
        Assert.Equal(lines.Length, imported.Length);
        Assert.Equal(lines.Select(line => (line.Start, line.End, line.Text)),
            imported.Select(line => (line.Start, line.End, line.Text)));
    }

    [Fact]
    public void PlainTextRoundTripsMoreThanSixteenMiCharacters()
    {
        var content = new string('x', 65536);
        var source = string.Join('\n', Enumerable.Repeat(content, 257));
        Assert.True(source.Length > 16 * 1024 * 1024);

        var imported = SubtitleTextFormat.ImportText(source);

        Assert.Equal(257, imported.Length);
        Assert.All(imported, line => Assert.Equal(content, line.Text));
        Assert.Equal(source, SubtitleTextFormat.WriteText(imported));
    }
}
