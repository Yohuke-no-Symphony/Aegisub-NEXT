using System.Text;
using AegiNext.SubtitleFormats;
using Spectre.Console.Testing;

namespace AegiNext.Cli.Tests;

public sealed class CliCommandTests
{
    [Fact]
    public async Task ExplicitAliasesWriteUtf8WithoutBom()
    {
        using var directory = new CliTestDirectory();
        var input = await directory.InputAsync();
        var output = directory.File("converted.srt");
        var service = new RecordingSubtitleConversionService();
        var console = Console(false);
        var exit = await new CliApplication(new(console, false), service).RunAsync(["convert", "-i", input, "--target", "srt", "-o", output]);
        Assert.Equal(0, exit);
        Assert.Equal(input, service.Input);
        Assert.Equal("srt", service.Target);
        var bytes = await File.ReadAllBytesAsync(output);
        Assert.Equal(Encoding.UTF8.GetBytes(service.Result.Text), bytes);
    }

    [Theory]
    [InlineData("--unknown")]
    [InlineData("--target")]
    [InlineData("--input")]
    public async Task MalformedArgumentsFailBeforeConversion(string argument)
    {
        var service = new RecordingSubtitleConversionService();
        var console = Console(false);
        var exit = await new CliApplication(new(console, false), service).RunAsync(["convert", argument]);
        Assert.Equal(2, exit);
        Assert.Equal(0, service.Calls);
    }

    [Theory]
    [InlineData("convert")]
    [InlineData("root")]
    public async Task RedirectedMissingArgumentsNeverPrompt(string command)
    {
        var service = new RecordingSubtitleConversionService();
        var console = Console(true);
        var exit = await new CliApplication(new(console, false), service).RunAsync(command == "root" ? [] : [command]);
        Assert.Equal(2, exit);
        Assert.Equal(0, service.Calls);
        Assert.Contains(command == "root" ? "Interactive prompts are disabled" : "interactive prompts are disabled", console.Output);
    }

    [Fact]
    public async Task NonInteractiveSwitchDisablesPromptOnTerminal()
    {
        var console = Console(true);
        var exit = await new CliApplication(new(console, true), new RecordingSubtitleConversionService()).RunAsync(["convert", "--non-interactive"]);
        Assert.Equal(2, exit);
    }

    [Fact]
    public async Task PartialArgumentsOnlyAskForMissingRequiredValue()
    {
        using var directory = new CliTestDirectory();
        var input = await directory.InputAsync();
        var console = Console(true);
        console.Input.PushTextWithEnter(input);
        var service = new RecordingSubtitleConversionService();
        var exit = await new CliApplication(new(console, true), service).RunAsync(["convert", "--target", "srt"]);
        Assert.Equal(0, exit);
        Assert.True(File.Exists(directory.File("input.converted.srt")));
        Assert.Contains("Input file path:", console.Output);
        Assert.DoesNotContain("Output file or directory (", console.Output);
    }

    [Fact]
    public async Task EmptySubcommandGuidesInputTargetAndOutput()
    {
        using var directory = new CliTestDirectory();
        var input = await directory.InputAsync();
        var output = directory.File("guided.ass");
        var console = Console(true);
        console.Input.PushTextWithEnter(input);
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushTextWithEnter(output);
        var service = new RecordingSubtitleConversionService();
        var exit = await new CliApplication(new(console, true), service).RunAsync(["convert"]);
        Assert.Equal(0, exit);
        Assert.Equal("ass", service.Target);
        Assert.True(File.Exists(output));
    }

    [Fact]
    public async Task EmptyRootSelectsOperationThenUsesSameRunner()
    {
        using var directory = new CliTestDirectory();
        var input = await directory.InputAsync();
        var console = Console(true);
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushTextWithEnter(input);
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.Enter);
        var service = new RecordingSubtitleConversionService();
        var exit = await new CliApplication(new(console, true), service).RunAsync([]);
        Assert.Equal(0, exit);
        Assert.Equal(1, service.Calls);
        Assert.True(File.Exists(directory.File("input.ass")));
    }

    [Fact]
    public async Task ExplicitInvalidTargetDoesNotAskToRepairIt()
    {
        var console = Console(true);
        var service = new RecordingSubtitleConversionService();
        var exit = await new CliApplication(new(console, true), service).RunAsync(["convert", "--target", "txt"]);
        Assert.Equal(2, exit);
        Assert.Equal(0, service.Calls);
    }

    [Fact]
    public async Task ExplicitInvalidOutputIsRejectedBeforeAskingForInput()
    {
        using var directory = new CliTestDirectory();
        var console = Console(true);
        var service = new RecordingSubtitleConversionService();
        var exit = await new CliApplication(new(console, true), service).RunAsync(["convert", "-o", directory.File("unsupported.txt")]);
        Assert.Equal(2, exit);
        Assert.Equal(0, service.Calls);
        Assert.DoesNotContain("Input file path:", console.Output);
    }

    [Fact]
    public async Task ExplicitOccupiedOutputIsRejectedBeforeAskingForInput()
    {
        using var directory = new CliTestDirectory();
        var output = await directory.InputAsync("occupied.srt", "keep");
        var console = Console(true);
        var service = new RecordingSubtitleConversionService();
        var exit = await new CliApplication(new(console, true), service).RunAsync(["convert", "-o", output]);
        Assert.Equal(2, exit);
        Assert.Equal(0, service.Calls);
        Assert.DoesNotContain("Input file path:", console.Output);
        Assert.Equal("keep", await File.ReadAllTextAsync(output));
    }

    [Fact]
    public async Task ScriptDiagnosticsRequireAcceptLossAndNeverCreateOutputDirectory()
    {
        using var directory = new CliTestDirectory();
        var input = await directory.InputAsync();
        var output = directory.File("not-created");
        var service = DiagnosticService();
        var console = Console(false);
        var exit = await new CliApplication(new(console, false), service).RunAsync(["convert", "-i", input, "--target", "srt", "-o", output]);
        Assert.Equal(2, exit);
        Assert.False(Directory.Exists(output));
        Assert.Contains("Srt.Appearance", console.Output);
        Assert.Contains("--accept-loss", console.Output);
    }

    [Fact]
    public async Task AcceptLossWritesAndStillDisplaysDiagnostics()
    {
        using var directory = new CliTestDirectory();
        var input = await directory.InputAsync();
        var console = Console(false);
        var exit = await new CliApplication(new(console, false), DiagnosticService()).RunAsync(["convert", "-i", input, "--target", "srt", "--accept-loss"]);
        Assert.Equal(0, exit);
        Assert.Contains("Srt.Appearance", console.Output);
        Assert.True(File.Exists(directory.File("input.converted.srt")));
    }

    [Theory]
    [InlineData("n", 130, false)]
    [InlineData("y", 0, true)]
    public async Task TerminalDiagnosticsRequireOneDecision(string answer, int expectedExit, bool writes)
    {
        using var directory = new CliTestDirectory();
        var input = await directory.InputAsync();
        var console = Console(true);
        console.Input.PushTextWithEnter(answer);
        var exit = await new CliApplication(new(console, true), DiagnosticService()).RunAsync(["convert", "-i", input, "--target", "srt"]);
        Assert.Equal(expectedExit, exit);
        Assert.Equal(writes, File.Exists(directory.File("input.converted.srt")));
    }

    [Fact]
    public async Task ExistingOutputIsRejectedBeforeParsingAndPreserved()
    {
        using var directory = new CliTestDirectory();
        var input = await directory.InputAsync();
        var output = await directory.InputAsync("existing.srt", "keep");
        var service = new RecordingSubtitleConversionService();
        var exit = await new CliApplication(new(Console(false), false), service).RunAsync(["convert", "-i", input, "--target", "srt", "-o", output]);
        Assert.Equal(2, exit);
        Assert.Equal(0, service.Calls);
        Assert.Equal("keep", await File.ReadAllTextAsync(output));
    }

    [Fact]
    public async Task ConcurrentOutputCreationIsPreservedAndTemporaryFileRemoved()
    {
        using var directory = new CliTestDirectory();
        var input = await directory.InputAsync();
        var output = directory.File("race.srt");
        var service = new RecordingSubtitleConversionService { BeforeReturn = () => File.WriteAllText(output, "other process") };
        var exit = await new CliApplication(new(Console(false), false), service).RunAsync(["convert", "-i", input, "--target", "srt", "-o", output]);
        Assert.Equal(1, exit);
        Assert.Equal("other process", await File.ReadAllTextAsync(output));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 130)]
    public async Task ExecutionErrorsAndCancellationHaveDistinctExitCodes(bool cancel, int expected)
    {
        using var directory = new CliTestDirectory();
        var input = await directory.InputAsync();
        var service = new RecordingSubtitleConversionService { Failure = cancel ? new OperationCanceledException() : new IOException("read failed") };
        var exit = await new CliApplication(new(Console(false), false), service).RunAsync(["convert", "-i", input, "--target", "srt"]);
        Assert.Equal(expected, exit);
        Assert.False(File.Exists(directory.File("input.converted.srt")));
    }

    [Fact]
    public async Task HelpAndVersionNeverStartWizard()
    {
        var console = Console(false);
        var service = new RecordingSubtitleConversionService();
        Assert.Equal(0, await new CliApplication(new(console, false), service).RunAsync(["--help"]));
        Assert.Contains("convert", console.Output);
        Assert.Equal(0, await new CliApplication(new(console, false), service).RunAsync(["--version"]));
        Assert.Equal(0, service.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HelpUsesEnglishCommandAndOptionDescriptions(bool convertHelp)
    {
        var console = Console(false);
        var service = new RecordingSubtitleConversionService();
        var exit = await new CliApplication(new(console, false), service).RunAsync(convertHelp ? ["convert", "--help"] : ["--help"]);
        Assert.Equal(0, exit);
        Assert.Equal(0, service.Calls);
        if (convertHelp)
        {
            Assert.Contains("Target subtitle format", console.Output);
            Assert.Contains("Input file path", console.Output);
            Assert.Contains("Accept conversion diagnostics", console.Output);
        }
        else
        {
            Assert.Contains("Convert ASS", console.Output);
        }
    }

    private static RecordingSubtitleConversionService DiagnosticService()
    {
        return new() { Result = new("converted", [new SubtitleFormatDiagnostic("Srt.Appearance", "Styling will be discarded.")]) };
    }

    private static TestConsole Console(bool interactive)
    {
        var console = new TestConsole();
        console.Profile.Capabilities.Interactive = interactive;
        console.Profile.Width = 120;
        return console;
    }
}
