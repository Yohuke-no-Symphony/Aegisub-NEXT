namespace AegiNext.Cli.Tests;

public sealed class CliPathsTests
{
    [Fact]
    public async Task OmittedSameFormatOutputUsesConvertedSuffixAndAvoidsExistingNames()
    {
        using var directory = new CliTestDirectory();
        var input = await directory.InputAsync();
        await directory.InputAsync("input.converted.srt");
        Directory.CreateDirectory(directory.File("input.converted-1.srt"));
        Assert.Equal(directory.File("input.converted-2.srt"), CliPaths.ResolveOutput(input, null, "srt"));
    }

    [Fact]
    public async Task ExtensionlessAndTrailingSeparatorOutputsCreateAutomaticNames()
    {
        using var directory = new CliTestDirectory();
        var input = await directory.InputAsync();
        var extensionless = directory.File("subtitles");
        var trailing = directory.File("directory.ass") + Path.DirectorySeparatorChar;
        Assert.Equal(Path.Combine(extensionless, "input.ass"), CliPaths.ResolveOutput(input, extensionless, "ass"));
        Assert.Equal(Path.Combine(directory.File("directory.ass"), "input.ass"), CliPaths.ResolveOutput(input, trailing, "ass"));
        Assert.False(Directory.Exists(extensionless));
    }

    [Fact]
    public async Task ExistingDirectoryWithExtensionIsStillDirectory()
    {
        using var directory = new CliTestDirectory();
        var input = await directory.InputAsync();
        var output = directory.File("folder.ass");
        Directory.CreateDirectory(output);
        Assert.Equal(Path.Combine(output, "input.srt"), CliPaths.ResolveOutput(input, output, "srt"));
    }

    [Fact]
    public async Task VideoOutputUsesProjectOutputDirectoryAndInjectedClock()
    {
        using var directory = new CliTestDirectory();
        var input = await directory.InputAsync("demo.aeginext");
        Assert.Equal(directory.File(Path.Combine("output", "demo-20261012-080910.mp4")),
            CliPaths.ResolveOutput(input, null, "mp4", true, new FixedCliTimeProvider()));
    }

    [Fact]
    public async Task ExplicitSameInputAndMismatchedExtensionsAreRejected()
    {
        using var directory = new CliTestDirectory();
        var input = await directory.InputAsync();
        Assert.Throws<CliUsageException>(() => CliPaths.ResolveOutput(input, input, "srt"));
        Assert.Throws<CliUsageException>(() => CliPaths.ResolveOutput(input, directory.File("wrong.ass"), "srt"));
        Assert.Throws<CliUsageException>(() => CliPaths.ResolveInput(input, ".aeginext"));
    }
}
