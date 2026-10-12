using AegiNext.Export.Worker;
using System.Text.Json;
using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Export;

namespace AegiNext.Export.Tests;

public sealed class ExportWireContentTests
{
    [Fact]
    public void LargeWorkerRequestsPreserveBodiesBeyondPreviousMessageBudget()
    {
        var line = new SubtitleLine { Text = new string('中', 12 * 1024 * 1024) };
        var document = new ProjectDocument
        {
            Subtitles = [line],
            Layers = [new() { Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End }]
        };
        var job = new ExportWorkerJob(JsonSerializer.SerializeToElement(document, ExportWire.Options), Path.GetTempPath(), Path.GetTempPath(), ".mkv",
            "ffmpeg", ExportWorkerOperation.BURN_IN, new(VideoCodec.H264, "medium", 18, AudioExportMode.None, 192000, RateControlMode: VideoRateControlMode.CRF),
            ProtocolVersion: ExportWire.VERSION);
        var request = JsonSerializer.Serialize(job, ExportWire.Options);
        Assert.True(request.Length > 64 * 1024 * 1024);

        var transmitted = JsonSerializer.Deserialize<ExportWorkerJob>(request, ExportWire.Options)!;
        ExportWire.ValidateJob(transmitted);
        var restored = ProjectStore.Deserialize(transmitted.Project);

        Assert.Equal(line.Id, Assert.Single(restored.Subtitles).Id);
        Assert.Equal(line.Text, restored.Subtitles[0].Text);
    }

    [Fact]
    public void RealWorkerRequestCarriesInlineFontsClipIdentityAndExactContentTime()
    {
        var font = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.FONT, "font.ttf");
        var line = new SubtitleLine
        {
            Text = "a😀", InlineSpans = [new(1, 2, new() { FontAssetId = font.Id, Underline = true, Fill = new(4, -0.2, 1, 0.5) })],
            Karaoke = [new(1, 2, new(1, 3), new(2, 3), SceneColor.White)
            {
                HighlightKind = KaraokeHighlightKind.OUTLINE_STEP,
            }]
        };
        var document = new ProjectDocument
        {
            Assets = [font], Subtitles = [line],
            Layers = [new() { Id = line.Id, Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End }]
        };
        ProjectValidator.Validate(document);
        var job = new ExportWorkerJob(JsonSerializer.SerializeToElement(document, ExportWire.Options), Path.GetTempPath(), Path.GetTempPath(), ".mkv",
            "ffmpeg", ExportWorkerOperation.BURN_IN, new(VideoCodec.H264, "medium", 18, AudioExportMode.None, 192000, RateControlMode: VideoRateControlMode.CRF),
            ProtocolVersion: ExportWire.VERSION);
        var transmitted = JsonSerializer.Deserialize<ExportWorkerJob>(JsonSerializer.Serialize(job, ExportWire.Options), ExportWire.Options)!;
        ExportWire.ValidateJob(transmitted);
        var restored = transmitted.Project.GetProperty("subtitles")[0];
        var clip = restored.GetProperty("karaoke")[0];
        Assert.Equal(ProjectDocument.CURRENT_VERSION, transmitted.Project.GetProperty("version").GetInt32());
        Assert.Equal(line.InlineSpans[0], restored.GetProperty("inlineSpans")[0].Deserialize<SubtitleInlineSpan>(ExportWire.Options));
        Assert.Equal(line.Karaoke[0].Id, clip.GetProperty("id").GetGuid());
        Assert.Equal("OUTLINE_STEP", clip.GetProperty("highlightKind").GetString());
        Assert.Equal(1, clip.GetProperty("start").GetProperty("numerator").GetInt64());
        Assert.Equal(3, clip.GetProperty("start").GetProperty("denominator").GetInt64());
        Assert.Equal(2, clip.GetProperty("end").GetProperty("numerator").GetInt64());
        Assert.Equal(3, clip.GetProperty("end").GetProperty("denominator").GetInt64());
        Assert.Equal(font, transmitted.Project.GetProperty("assets")[0].Deserialize<ProjectAsset>(ExportWire.Options));
    }
}
