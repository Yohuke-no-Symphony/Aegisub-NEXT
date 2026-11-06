using System.Globalization;
using System.Text.Json;
using AegiNext.Core.Projects;
using AegiNext.Export.Worker;

namespace AegiNext.Export.Tests;

public sealed class AudioExportWireTests
{
    [Fact]
    public void AudioRequestsRoundTripWithoutVideoOrFontOptions()
    {
        var job = CreateJob();
        var restored = JsonSerializer.Deserialize<ExportWorkerJob>(JsonSerializer.Serialize(job, ExportWire.Options), ExportWire.Options)!;

        ExportWire.ValidateJob(restored);

        Assert.Equal(ExportWorkerOperation.AUDIO, restored.Operation);
        Assert.Equal(AudioFileFormat.Flac, restored.Audio!.Format);
        Assert.Null(restored.Video);
        Assert.Empty(restored.FontFamilies);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("video")]
    [InlineData("wrong-operation")]
    [InlineData("legacy")]
    [InlineData("invalid-format")]
    [InlineData("invalid-bitrate")]
    public async Task InvalidPayloadsAreRejectedBeforeLoadingTheProject(string invalid)
    {
        var job = CreateJob();
        job = invalid switch
        {
            "missing" => job with { Audio = null },
            "video" => job with { Video = new(VideoCodec.H264, "medium", 20, AudioExportMode.None, 192000) },
            "wrong-operation" => job with { Operation = ExportWorkerOperation.SUBTITLE_MUX },
            "legacy" => job with { ProtocolVersion = ExportWire.VERSION - 1 },
            "invalid-format" => job,
            "invalid-bitrate" => job with { Audio = new(AudioFileFormat.Mp3, 0) },
            _ => throw new ArgumentOutOfRangeException(nameof(invalid))
        };
        var serialized = JsonSerializer.Serialize(job, ExportWire.Options);
        if (invalid == "invalid-format")
        {
            serialized = serialized.Replace("\"Flac\"", "\"Unsupported\"", StringComparison.Ordinal);
        }

        using var input = new StringReader(serialized);
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
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("unverified")]
    [InlineData("wrong-operation")]
    [InlineData("wrong-codec")]
    [InlineData("video")]
    [InlineData("empty-audio")]
    [InlineData("wrong-source")]
    public void InvalidCompletionEvidenceCannotCommit(string invalid)
    {
        using var directory = new ExportTestDirectory();
        var request = AudioExportRequestTests.CreateRequest(directory.Path, AudioFileFormat.Flac);
        var receipt = new AudioExportReceipt(0, "flac", 48000, 2, true);
        var message = new ExportWorkerMessage("complete", Operation: ExportWorkerOperation.AUDIO, Audio: receipt);
        message = invalid switch
        {
            "missing" => message with { Audio = null },
            "unverified" => message with { Audio = receipt with { OutputVerified = false } },
            "wrong-operation" => message with { Operation = ExportWorkerOperation.BURN_IN },
            "wrong-codec" => message with { Audio = receipt with { Codec = "aac" } },
            "video" => message with { Frames = 1 },
            "empty-audio" => message with { Audio = receipt with { SampleRate = 0 } },
            "wrong-source" => message with { Audio = receipt with { SourceStreamIndex = 3 } },
            _ => throw new ArgumentOutOfRangeException(nameof(invalid))
        };

        Assert.Throws<InvalidDataException>(() => AudioExporter.ValidateCompleted(request, message));
    }

    private static ExportWorkerJob CreateJob() => new(JsonSerializer.SerializeToElement(new ProjectDocument(), ExportWire.Options),
        Path.GetTempPath(), Path.GetTempPath(), ".flac", "ffmpeg", ExportWorkerOperation.AUDIO, ProtocolVersion: ExportWire.VERSION)
    {
        Audio = new(AudioFileFormat.Flac, 192000)
    };
}
