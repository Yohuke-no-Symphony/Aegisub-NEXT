using System.Text;
using AegiNext.Core.Timing;
using AegiNext.SubtitleFormats;
using Spectre.Console.Testing;

namespace AegiNext.Cli.Tests;

public sealed class LrcConversionTests
{
    [Fact]
    public async Task Utf8BomEnhancedLrcConvertsToSrtWithIdTagDiagnostics()
    {
        using var directory = new CliTestDirectory();
        var input = directory.File("song.lrc");
        await File.WriteAllTextAsync(input, "[ti:Song]\n[00:01.00]<00:01.00>你好 <00:01.50>世界\n[00:03.00]\n", new UTF8Encoding(true, true));
        var result = await new SubtitleConversionService().ConvertAsync(input, "srt", CancellationToken.None);
        var line = Assert.Single(SubtitleTextFormat.ParseSrt(result.Text));
        Assert.Equal("你好 世界", line.Text);
        Assert.Equal(new MediaTime(3), line.End);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Lrc.Metadata");
    }

    [Fact]
    public async Task SrtConvertsToStandardAndEnhancedLrc()
    {
        using var directory = new CliTestDirectory();
        var input = await directory.InputAsync();
        var service = new SubtitleConversionService();
        var text = SubtitleTextFormat.ParseSrt(await File.ReadAllTextAsync(input))[0].Text;
        Assert.Equal($"[00:00.00]{text}\n[00:02.00]\n", (await service.ConvertAsync(input, "lrc", CancellationToken.None)).Text);
        Assert.Equal($"[00:00.00]<00:00.00>{text}<00:02.00>\n", (await service.ConvertAsync(input, "elrc", CancellationToken.None)).Text);
    }

    [Theory]
    [InlineData("lrc")]
    [InlineData("elrc")]
    public async Task LrcTargetsWriteDotLrcFiles(string target)
    {
        using var directory = new CliTestDirectory();
        var input = await directory.InputAsync();
        var service = new RecordingSubtitleConversionService();
        var exit = await new CliApplication(new(new TestConsole(), false), service).RunAsync(["convert", "-i", input, "--target", target.ToUpperInvariant()]);
        Assert.Equal(0, exit);
        Assert.Equal(target, service.Target);
        Assert.True(File.Exists(directory.File("input.lrc")));
    }

    [Fact]
    public async Task ElrcRejectsNonLrcOutputExtension()
    {
        using var directory = new CliTestDirectory();
        var input = await directory.InputAsync();
        var exit = await new CliApplication(new(new TestConsole(), false), new RecordingSubtitleConversionService())
            .RunAsync(["convert", "-i", input, "--target", "elrc", "-o", directory.File("out.srt")]);
        Assert.Equal(2, exit);
    }
}
