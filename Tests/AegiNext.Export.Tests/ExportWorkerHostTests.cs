using System.Globalization;
using System.Text.Json;
using AegiNext.Core.Projects;
using AegiNext.Export.Worker;

namespace AegiNext.Export.Tests;

/// <summary>独立 worker 公共入口的协议验证与工程加载错误边界。</summary>
public sealed class ExportWorkerHostTests
{
    /// <summary>空输入在加载工程和原生模块前产生唯一错误回执。</summary>
    [Fact]
    public async Task MissingInputReturnsProtocolErrorWithoutLoadingProject()
    {
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var errors = new StringWriter(CultureInfo.InvariantCulture);
        var loaded = false;

        var exitCode = await ExportWorkerHost.RunAsync(input, output, errors, _ =>
        {
            loaded = true;
            return new();
        });

        Assert.Equal(1, exitCode);
        Assert.False(loaded);
        var message = JsonSerializer.Deserialize<ExportWorkerMessage>(output.ToString(), ExportWire.Options)!;
        Assert.Equal("error", message.Type);
        Assert.Contains("snapshot request", message.Error, StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidDataException), errors.ToString(), StringComparison.Ordinal);
    }

    /// <summary>旧协议必须在调用应用反序列化前拒绝，有效协议则保留应用加载错误。</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(5, false)]
    [InlineData(ExportWire.VERSION, true)]
    public async Task ProtocolValidationPrecedesTheApplicationProjectLoader(int version, bool expectedLoad)
    {
        var job = new ExportWorkerJob(JsonSerializer.SerializeToElement(new ProjectDocument(), ExportWire.Options), Path.GetTempPath(), Path.GetTempPath(), ".mkv",
            "ffmpeg", ExportWorkerOperation.BURN_IN, new(VideoCodec.H264, "medium", 20, AudioExportMode.None, 192000, RateControlMode: VideoRateControlMode.CRF),
            ProtocolVersion: version);
        using var input = new StringReader(JsonSerializer.Serialize(job, ExportWire.Options));
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var errors = new StringWriter(CultureInfo.InvariantCulture);
        var loaded = false;

        var exitCode = await ExportWorkerHost.RunAsync(input, output, errors, project =>
        {
            loaded = true;
            Assert.Equal(job.Project.GetRawText(), project.GetRawText());
            throw new InvalidDataException("application project rejected");
        });

        Assert.Equal(1, exitCode);
        Assert.Equal(expectedLoad, loaded);
        var message = JsonSerializer.Deserialize<ExportWorkerMessage>(output.ToString(), ExportWire.Options)!;
        Assert.Equal("error", message.Type);
        Assert.Contains(expectedLoad ? "application project rejected" : "protocol", message.Error, StringComparison.Ordinal);
    }
}
