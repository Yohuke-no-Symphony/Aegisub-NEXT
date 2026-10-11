using System.Text.Json;
using AegiNext.Core.Projects;
using AegiNext.Export;

namespace AegiNext.Export.Tests;

public sealed class ExportWireFontVariantTests
{
    [Fact]
    public void WorkerJobPreservesBaseInlineAndTrackVariants()
    {
        var style = new SubtitleStyle { FontFamily = "Example Sans", FontVariant = new() { Name = "SemiBold", Weight = 600 } };
        var line = new SubtitleLine
        {
            Text = "AB", Style = style,
            InlineSpans = [new(1, 1, new() { FontVariant = new() { Name = "Black", PostScriptName = "ExampleSans-Black", Weight = 900 }, Bold = true })]
        };
        var document = new ProjectDocument
        {
            Tracks = [ProjectTrack.Default with { DefaultStyle = style, StylePresetId = Guid.NewGuid(), StylePresetName = "Named face" }],
            Subtitles = [line],
            Layers = [new() { Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End }]
        };
        ProjectValidator.Validate(document);
        var job = new ExportWorkerJob(JsonSerializer.SerializeToElement(document, ExportWire.Options), Path.GetTempPath(),
            Path.GetTempPath(), ".mkv", VideoCodec.H264, "medium", 18, AudioExportMode.None, 192000, "ffmpeg",
            RateControlMode: VideoRateControlMode.CRF, ProtocolVersion: ExportWire.VERSION);
        var received = JsonSerializer.Deserialize<ExportWorkerJob>(JsonSerializer.Serialize(job, ExportWire.Options), ExportWire.Options)!;
        ExportWire.ValidateJob(received);
        Assert.Equal(style, received.Project.GetProperty("tracks")[0].GetProperty("defaultStyle").Deserialize<SubtitleStyle>(ExportWire.Options));
        Assert.Equal(style, received.Project.GetProperty("subtitles")[0].GetProperty("style").Deserialize<SubtitleStyle>(ExportWire.Options));
        Assert.Equal(line.InlineSpans[0].Style, received.Project.GetProperty("subtitles")[0].GetProperty("inlineSpans")[0].GetProperty("style")
            .Deserialize<SubtitleInlineStyleOverride>(ExportWire.Options));
    }
}
