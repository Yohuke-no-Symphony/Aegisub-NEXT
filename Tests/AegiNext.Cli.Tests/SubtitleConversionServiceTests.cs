using System.Text;
using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.SubtitleFormats;
using Spectre.Console.Testing;

namespace AegiNext.Cli.Tests;

public sealed class SubtitleConversionServiceTests
{
    [Fact]
    public async Task Utf8BomSrtPreservesUnicodeMultilineAndTiming()
    {
        using var directory = new CliTestDirectory();
        var input = directory.File("input.srt");
        const string SOURCE = "1\n00:00:01,003 --> 00:00:02,007\n第一行\nمرحبا 😀\n";
        await File.WriteAllTextAsync(input, SOURCE, new UTF8Encoding(true, true));
        var result = await new SubtitleConversionService().ConvertAsync(input, "srt", CancellationToken.None);
        var line = Assert.Single(SubtitleTextFormat.ParseSrt(result.Text));
        Assert.Equal("第一行\nمرحبا 😀", line.Text);
        Assert.Equal(new MediaTime(1003, 1000), line.Start);
        Assert.Equal(new MediaTime(2007, 1000), line.End);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task MalformedUtf8IsRejectedWithoutReplacementCharactersOrOutput()
    {
        using var directory = new CliTestDirectory();
        var input = directory.File("input.srt");
        await File.WriteAllBytesAsync(input, [0xC3, 0x28]);
        var console = new TestConsole();
        var exit = await new CliApplication(new(console, false)).RunAsync(["convert", "-i", input, "--target", "srt"]);
        Assert.Equal(1, exit);
        Assert.False(File.Exists(directory.File("input.converted.srt")));
    }

    [Theory]
    [InlineData("invalid.ass", "[Script Info]\nPlayResX: 0\nPlayResY: 360\n[Events]\n", "ASS PlayRes values must be positive.")]
    [InlineData("invalid.srt", "1\n00:00:02,000 --> 00:00:01,000\nHello\n", "SRT end times must be later than start times.")]
    [InlineData("invalid.aeginext", "{", "The project JSON format is invalid.")]
    public async Task InvalidFormatErrorsAreReportedInEnglishWithoutOutput(string name, string source, string expectedMessage)
    {
        using var directory = new CliTestDirectory();
        var input = await directory.InputAsync(name, source);
        var output = directory.File("converted.srt");
        var console = new TestConsole();
        var exit = await new CliApplication(new(console, false)).RunAsync(["convert", "-i", input, "--target", "srt", "-o", output]);
        Assert.Equal(1, exit);
        Assert.Contains($"Error: {expectedMessage}", console.Output);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task ProjectToSrtUsesConfirmedExternalTimeline()
    {
        using var directory = new CliTestDirectory();
        var input = directory.File("input.aeginext");
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, "missing-media.mp4");
        var document = ProjectEditingOperations.ImportSubtitleLines(new()
        {
            Assets = [asset],
            Media = new(asset.Id, 0, null, new(5)) { PlaybackOrigin = new(2) }
        }, [new SubtitleLine { Start = new(1), End = new(2), Text = "字幕" }], "Imported");
        await ProjectStore.CreateAsync(document, input);
        var result = await new SubtitleConversionService().ConvertAsync(input, "srt", CancellationToken.None);
        var line = Assert.Single(SubtitleTextFormat.ParseSrt(result.Text));
        Assert.Equal(new MediaTime(4), line.Start);
        Assert.Equal(new MediaTime(5), line.End);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task UnknownProjectPlaybackOriginReturnsDiagnosticAndZeroOffset()
    {
        using var directory = new CliTestDirectory();
        var input = directory.File("input.aeginext");
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, "missing-media.mp4");
        var document = ProjectEditingOperations.ImportSubtitleLines(new()
        {
            Assets = [asset],
            Media = new(asset.Id, 0, null, new(5))
        }, [new SubtitleLine { Start = new(1), End = new(2), Text = "字幕" }], "Imported");
        await ProjectStore.CreateAsync(document, input);
        var result = await new SubtitleConversionService().ConvertAsync(input, "srt", CancellationToken.None);
        var diagnostic = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == "Subtitle.PlaybackOriginUnknown");
        Assert.StartsWith("The project media playback origin is unconfirmed", diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(new MediaTime(1), Assert.Single(SubtitleTextFormat.ParseSrt(result.Text)).Start);
    }

    [Fact]
    public async Task AssToAssRetainsSourceCanvas()
    {
        using var directory = new CliTestDirectory();
        var input = await directory.InputAsync("source.ass", AssSource("Hello"));
        var result = await new SubtitleConversionService().ConvertAsync(input, "ass", CancellationToken.None);
        var parsed = AssSubtitleFormat.ParseAtSourceResolution(result.Text);
        Assert.Equal(640, parsed.Width);
        Assert.Equal(360, parsed.Height);
        Assert.Equal("Hello", Assert.Single(parsed.Result.Lines).Text);
    }

    [Fact]
    public async Task AssMasksAndAnimationReachSrtLossAnalysis()
    {
        using var directory = new CliTestDirectory();
        var input = await directory.InputAsync("source.ass", AssSource(@"{\clip(0,0,200,100)\move(10,20,100,120)}Hello"));
        var result = await new SubtitleConversionService().ConvertAsync(input, "srt", CancellationToken.None);
        var mask = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == "Srt.Mask");
        var animation = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == "Srt.Animation");
        Assert.StartsWith("SRT does not support clipping masks", mask.Message, StringComparison.Ordinal);
        Assert.StartsWith("SRT does not support clip or mask animation", animation.Message, StringComparison.Ordinal);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Srt.Appearance" &&
            diagnostic.Message.StartsWith("SRT preserves only text and timing", StringComparison.Ordinal));
        Assert.Equal("Hello", Assert.Single(SubtitleTextFormat.ParseSrt(result.Text)).Text);
    }

    [Fact]
    public async Task MissingAssResolutionAxisAddsImportDiagnostic()
    {
        using var directory = new CliTestDirectory();
        var input = await directory.InputAsync("source.ass", AssSource("Hello").Replace("PlayResY: 360\n", "", StringComparison.Ordinal));
        var result = await new SubtitleConversionService().ConvertAsync(input, "srt", CancellationToken.None);
        var diagnostic = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == "Ass.PlayRes");
        Assert.StartsWith("Missing ASS PlayRes axes use default dimensions", diagnostic.Message, StringComparison.Ordinal);
    }

    private static string AssSource(string text)
    {
        return "[Script Info]\nScriptType: v4.00+\nPlayResX: 640\nPlayResY: 360\nScaledBorderAndShadow: yes\n" +
            "[V4+ Styles]\nFormat: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding\n" +
            "Style: Default,Arial,20,&H00FFFFFF,&H000000FF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,0,0,2,10,10,10,1\n" +
            "[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n" +
            "Dialogue: 0,0:00:00.00,0:00:02.00,Default,,0,0,0,," + text;
    }
}
