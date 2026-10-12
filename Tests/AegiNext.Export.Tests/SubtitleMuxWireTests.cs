using System.Globalization;
using System.Text.Json;
using AegiNext.Core.Projects;
using AegiNext.Export.Worker;

namespace AegiNext.Export.Tests;

public sealed class SubtitleMuxWireTests
{
    [Fact]
    public void MuxRequestsHaveAnExplicitOperationAndCarryNoEncodingOptions()
    {
        var job = CreateJob();
        var text = JsonSerializer.Serialize(job, ExportWire.Options);
        var restored = JsonSerializer.Deserialize<ExportWorkerJob>(text, ExportWire.Options)!;
        ExportWire.ValidateJob(restored);
        Assert.Equal(ExportWorkerOperation.SUBTITLE_MUX, restored.Operation);
        Assert.Null(restored.Video);
        Assert.Equal(ExportWire.VERSION, restored.ProtocolVersion);
        Assert.DoesNotContain("rate_mode", text, StringComparison.Ordinal);
        Assert.DoesNotContain("decodeMode", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData((int)ExportWorkerOperation.UNKNOWN, false, ExportWire.VERSION)]
    [InlineData((int)ExportWorkerOperation.BURN_IN, false, ExportWire.VERSION)]
    [InlineData((int)ExportWorkerOperation.SUBTITLE_MUX, true, ExportWire.VERSION)]
    [InlineData((int)ExportWorkerOperation.SUBTITLE_MUX, false, 3)]
    [InlineData((int)ExportWorkerOperation.SUBTITLE_MUX, false, 0)]
    public async Task InvalidOperationPayloadsAndLegacyProtocolsAreRejectedBeforeProjectLoading(
        int operation, bool encodingOptions, int version)
    {
        var job = CreateJob() with
        {
            Operation = (ExportWorkerOperation)operation, ProtocolVersion = version,
            Video = encodingOptions ? new(VideoCodec.H264, "medium", 20, AudioExportMode.Copy, 192000,
                RateControlMode: VideoRateControlMode.CRF) : null
        };
        using var input = new StringReader(JsonSerializer.Serialize(job, ExportWire.Options));
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var errors = new StringWriter(CultureInfo.InvariantCulture);
        var loaded = false;
        var exit = await ExportWorkerHost.RunAsync(input, output, errors, _ =>
        {
            loaded = true;
            return new();
        });
        Assert.Equal(1, exit);
        Assert.False(loaded);
        var receipt = JsonSerializer.Deserialize<ExportWorkerMessage>(output.ToString(), ExportWire.Options)!;
        Assert.Equal("error", receipt.Type);
        Assert.Contains("协议", receipt.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData((int)ExportWorkerOperation.UNKNOWN, true, false)]
    [InlineData((int)ExportWorkerOperation.BURN_IN, true, false)]
    [InlineData((int)ExportWorkerOperation.SUBTITLE_MUX, false, false)]
    [InlineData((int)ExportWorkerOperation.SUBTITLE_MUX, true, true)]
    public void MissingVerificationWrongOperationsAndEncodingEvidenceCannotCommit(
        int operation, bool verified, bool encodingEvidence)
    {
        using var directory = new ExportTestDirectory();
        var request = new SubtitleMuxRequest(new(), directory.Path, Path.Combine(directory.Path, "output.mkv"));
        var receipt = new ExportWorkerMessage("complete", Operation: (ExportWorkerOperation)operation,
            Encoder: encodingEvidence ? "libx264" : null, SubtitleMux: new(1, 2, 1, 4, "subrip", verified));
        Assert.Throws<InvalidDataException>(() => SubtitleMuxExporter.ValidateCompleted(request, receipt));
    }

    private static ExportWorkerJob CreateJob() => new(JsonSerializer.SerializeToElement(new ProjectDocument(), ExportWire.Options),
        Path.GetTempPath(), Path.GetTempPath(), ".mkv", "ffmpeg", ExportWorkerOperation.SUBTITLE_MUX, ProtocolVersion: ExportWire.VERSION);
}
