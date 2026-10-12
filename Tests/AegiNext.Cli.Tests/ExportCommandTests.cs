using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Export;
using AegiNext.Media.Decoding;
using Spectre.Console.Testing;

namespace AegiNext.Cli.Tests;

public sealed class ExportCommandTests
{
    [Theory]
    [InlineData("auto", VideoRateControlMode.CRF, 20, ".mp4")]
    [InlineData("h264", VideoRateControlMode.CRF, 20, ".mp4")]
    [InlineData("hevc", VideoRateControlMode.CRF, 20, ".mp4")]
    [InlineData("vp8", VideoRateControlMode.CQ, 32, ".mkv")]
    [InlineData("vp9", VideoRateControlMode.CRF, 32, ".mkv")]
    [InlineData("av1", VideoRateControlMode.CRF, 32, ".mkv")]
    public async Task CodecDefaultsAndOutputDirectoryReachExportService(string codec, VideoRateControlMode mode, int quality, string extension)
    {
        using var directory = new CliTestDirectory();
        var input = await CreateInputAsync(directory);
        var console = CreateConsole(false);
        var service = new RecordingVideoExportService();
        var exit = await CreateApplication(console, false, service).RunAsync(["export", "-i", input, "--encoding", codec]);
        Assert.Equal(0, exit);
        var request = Assert.IsType<VideoExportRequest>(service.Request);
        Assert.Equal(mode, request.RateControlMode);
        Assert.Equal(quality, request.Quality);
        Assert.Equal(Path.Combine(directory.Path, "output"), Path.GetDirectoryName(request.OutputPath));
        Assert.Equal(extension, Path.GetExtension(request.OutputPath));
        Assert.Equal(directory.Path, request.ProjectDirectory);
        Assert.Equal(VideoDecodeMode.Auto, request.DecodeMode);
        Assert.Contains("test-encoder", console.Output);
    }

    [Theory]
    [InlineData("--crf", "--vbr")]
    [InlineData("--vbr", "--quality", "20")]
    [InlineData("--crf", "--video-bitrate", "8")]
    [InlineData("--audio", "none", "--audio-bitrate", "192")]
    [InlineData("--audio", "copy", "--audio-bitrate", "192")]
    [InlineData("--encoding", "h264", "--cq")]
    [InlineData("--encoding", "vp8", "--crf")]
    [InlineData("--encoding", "vp8", "--use-gpu")]
    [InlineData("--container", "webm", "--encoding", "h264")]
    [InlineData("--container", "webm", "--encoding", "vp9", "--audio", "aac")]
    [InlineData("--container", "mp4", "-o", "movie.mkv")]
    [InlineData("--encoding", "h265")]
    [InlineData("--speed", "turbo")]
    [InlineData("--decode", "invalid")]
    [InlineData("--vbr", "--video-bitrate", "8.0001")]
    [InlineData("--vbr", "--video-bitrate", "8,5")]
    [InlineData("--vbr", "--video-bitrate", "NaN")]
    [InlineData("--vbr", "--video-bitrate", "200.001")]
    [InlineData("--quality", "52")]
    [InlineData("--encoding", "av1", "--quality", "0")]
    [InlineData("--audio", "aac", "--audio-bitrate", "513")]
    public async Task ExplicitConflictsFailBeforeMissingInputPrompt(params string[] arguments)
    {
        var console = CreateConsole(true);
        var service = new RecordingVideoExportService();
        var exit = await CreateApplication(console, true, service).RunAsync(["export", .. arguments]);
        Assert.Equal(2, exit);
        Assert.Null(service.Request);
        Assert.DoesNotContain("工程文件路径", console.Output);
    }

    [Fact]
    public async Task ExistingExplicitOutputFailsBeforeInputPrompt()
    {
        using var directory = new CliTestDirectory();
        var output = await directory.InputAsync("movie.mp4", "keep");
        var console = CreateConsole(true);
        var service = new RecordingVideoExportService();
        var exit = await CreateApplication(console, true, service).RunAsync(["export", "-o", output]);
        Assert.Equal(2, exit);
        Assert.Null(service.Request);
        Assert.DoesNotContain("工程文件路径", console.Output);
        Assert.Equal("keep", await File.ReadAllTextAsync(output));
    }

    [Fact]
    public async Task PartialVbrDoesNotAskForDefaultBitrateOrOtherOptions()
    {
        using var directory = new CliTestDirectory();
        var input = await CreateInputAsync(directory);
        var console = CreateConsole(true);
        var service = new RecordingVideoExportService();
        var exit = await CreateApplication(console, true, service).RunAsync(["export", "-i", input, "--vbr"]);
        Assert.Equal(0, exit);
        Assert.Equal(8_000_000, service.Request!.VideoBitrate);
        Assert.Equal(VideoRateControlMode.VBR, service.Request.RateControlMode);
        Assert.DoesNotContain("视频码率（", console.Output);
        Assert.DoesNotContain("视频编码", console.Output);
        Assert.Contains("8 Mbps", console.Output);
    }

    [Fact]
    public async Task PartialCommandOnlyPromptsForInput()
    {
        using var directory = new CliTestDirectory();
        var input = await CreateInputAsync(directory);
        var console = CreateConsole(true);
        console.Input.PushTextWithEnter(input);
        var service = new RecordingVideoExportService();
        var exit = await CreateApplication(console, true, service).RunAsync(["export", "--vbr", "--video-bitrate", "8.125", "--audio", "opus", "--audio-bitrate", "64", "--decode", "software"]);
        Assert.Equal(0, exit);
        Assert.Equal(8_125_000, service.Request!.VideoBitrate);
        Assert.Equal(64_000, service.Request.AudioBitrate);
        Assert.Equal(VideoDecodeMode.Software, service.Request.DecodeMode);
        Assert.DoesNotContain("输出文件或目录（", console.Output);
    }

    [Fact]
    public async Task ExplicitOutputExtensionChoosesContainer()
    {
        using var directory = new CliTestDirectory();
        var input = await CreateInputAsync(directory);
        var output = directory.File("movie.mkv");
        var service = new RecordingVideoExportService();
        var exit = await CreateApplication(CreateConsole(false), false, service).RunAsync(["export", "-i", input, "-o", output]);
        Assert.Equal(0, exit);
        Assert.Equal(output, service.Request!.OutputPath);
    }

    [Fact]
    public async Task EmptyExportWizardCollectsApplicableOptions()
    {
        using var directory = new CliTestDirectory();
        var input = await CreateInputAsync(directory);
        var console = CreateConsole(true);
        console.Input.PushTextWithEnter(input);
        console.Input.PushKey(ConsoleKey.Enter);
        if (VideoExportCapabilities.Get(VideoCodec.Auto).SupportsHardwareOnCurrentPlatform)
        {
            console.Input.PushTextWithEnter("n");
        }
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.Enter);
        var service = new RecordingVideoExportService();
        var exit = await CreateApplication(console, true, service).RunAsync(["export"]);
        Assert.Equal(0, exit);
        Assert.Equal(VideoRateControlMode.CRF, service.Request!.RateControlMode);
        Assert.Equal(20, service.Request.Quality);
        Assert.Contains("输出容器", console.Output);
        Assert.DoesNotContain("音频码率（", console.Output);
    }

    [Fact]
    public async Task GpuRequestIsStrictAndDefaultsToVbr()
    {
        using var directory = new CliTestDirectory();
        var input = await CreateInputAsync(directory);
        var service = new RecordingVideoExportService { Failure = new InvalidOperationException("GPU unavailable") };
        var console = CreateConsole(false);
        var supported = VideoExportCapabilities.Get(VideoCodec.H264).SupportsHardwareOnCurrentPlatform;
        var exit = await CreateApplication(console, false, service).RunAsync(["export", "-i", input, "--encoding", "h264", "--use-gpu"]);
        Assert.Equal(supported ? 1 : 2, exit);
        if (supported)
        {
            Assert.Equal(VideoEncodingMode.HARDWARE, service.Request!.EncodingMode);
            Assert.Equal(VideoRateControlMode.VBR, service.Request.RateControlMode);
            Assert.Contains("GPU unavailable", console.Output);
        }
        else
        {
            Assert.Null(service.Request);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingInputCannotPromptWhenDisabled(bool nonInteractive)
    {
        var console = CreateConsole(true);
        var service = new RecordingVideoExportService();
        var exit = await CreateApplication(console, nonInteractive, service).RunAsync(nonInteractive
            ? ["export", "--non-interactive"] : ["export"]);
        Assert.Equal(2, exit);
        Assert.Null(service.Request);
    }

    [Fact]
    public async Task CancelledExportUsesCancellationExitCode()
    {
        using var directory = new CliTestDirectory();
        var input = await CreateInputAsync(directory);
        var service = new RecordingVideoExportService { Failure = new OperationCanceledException() };
        var exit = await CreateApplication(CreateConsole(false), false, service).RunAsync(["export", "-i", input]);
        Assert.Equal(130, exit);
    }

    private static CliApplication CreateApplication(TestConsole console, bool interactive, RecordingVideoExportService service)
    {
        var runtime = new CliRuntime(console, interactive);
        var app = new CliApplication(runtime);
        var runner = new ExportRunner(runtime, service);
        app.RegisterInstance(runner);
        app.RegisterCommand<ExportCommand>("export", "项目导出视频", cancellationToken => runner.RunAsync(new(), cancellationToken));
        return app;
    }

    private static TestConsole CreateConsole(bool interactive)
    {
        var console = new TestConsole();
        console.Profile.Capabilities.Interactive = interactive;
        return console;
    }

    private static async Task<string> CreateInputAsync(CliTestDirectory directory)
    {
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, string.Empty, ExternalPath: directory.File("source.mp4"));
        var project = new ProjectDocument { Assets = [asset], Media = new(asset.Id, 0, null, MediaTime.Zero) };
        var input = directory.File("project.aeginext");
        await ProjectStore.CreateAsync(project, input);
        return input;
    }
}
