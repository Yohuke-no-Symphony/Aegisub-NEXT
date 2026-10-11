using System.Text.Json;
using AegiNext.Core.Projects;
using AegiNext.Export;

namespace AegiNext.Export.Tests;

public sealed class ExportWireAnimationTests
{
    [Fact]
    public void RealWorkerWirePreservesKaraokeVisualSnapshotAndHdrValues()
    {
        var highlight = KaraokeHighlightStyle.FromStyle(Guid.NewGuid(), "HDR karaoke", new()
        {
            Fill = new(4, -0.2, 2, 0.4), Stroke = new(0, 3, 1), StrokeWidth = 5,
            ShadowColor = new(1, 0, 2, 0.5), ShadowOffset = new(-3, 8), ShadowBlur = 4
        });
        var line = new SubtitleLine { Text = "ab", KaraokeStyle = highlight, Karaoke = [new(0, 2, new(0), new(1), SceneColor.White)] };
        var project = new ProjectDocument
        {
            Subtitles = [line],
            Layers = [new() { Id = line.Id, Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End }]
        };
        ProjectValidator.Validate(project);
        var job = new ExportWorkerJob(JsonSerializer.SerializeToElement(project, ExportWire.Options), Path.GetTempPath(),
            Path.GetTempPath(), ".mkv", VideoCodec.H264, "medium", 18, AudioExportMode.None, 192000, "ffmpeg",
            RateControlMode: VideoRateControlMode.CRF, ProtocolVersion: ExportWire.VERSION);
        var restoredJob = JsonSerializer.Deserialize<ExportWorkerJob>(JsonSerializer.Serialize(job, ExportWire.Options), ExportWire.Options)!;
        ExportWire.ValidateJob(restoredJob);
        var restoredLine = restoredJob.Project.GetProperty("subtitles")[0];
        Assert.Equal(highlight, restoredLine.GetProperty("karaokeStyle").Deserialize<KaraokeHighlightStyle>(ExportWire.Options));
        var segment = restoredLine.GetProperty("karaoke")[0];
        Assert.Equal(0, segment.GetProperty("utf16Start").GetInt32());
        Assert.Equal(2, segment.GetProperty("utf16Length").GetInt32());
        Assert.Equal(0, segment.GetProperty("start").GetProperty("numerator").GetInt64());
        Assert.Equal(1, segment.GetProperty("start").GetProperty("denominator").GetInt64());
        Assert.Equal(1, segment.GetProperty("end").GetProperty("numerator").GetInt64());
        Assert.Equal(1, segment.GetProperty("end").GetProperty("denominator").GetInt64());
        Assert.Equal(SceneColor.White, segment.GetProperty("highlightColor").Deserialize<SceneColor>(ExportWire.Options));
        Assert.Equal(64, restoredLine.GetProperty("style").GetProperty("fontSize").GetDouble());
    }

    [Fact]
    public void RealWorkerWireSerializesScalarVectorColorAndIndependentCurvesWithoutApplicationConverters()
    {
        var project = new ProjectDocument
        {
            Layers = [new()
            {
                Kind = LayerKind.SHAPE,
                Shape = new(ShapeKind.RECTANGLE, 100, 60),
                Transform = new() { Position = new(20, -30), Scale = new(2, 3), Pivot = new(7, 9) },
                Tracks =
                [
                    new(AnimationProperty.POSITION, [new(new(1), new ScenePoint(40, 50), KeyframeInterpolation.EASE_IN)
                    {
                        CurveStart = 0.1, CurveEnd = 0.9, VectorCurve = new(KeyframeInterpolation.EASE_OUT, 0.2, 0.8)
                    }]),
                    new(AnimationProperty.OPACITY, [new(new(1), 0.75)]),
                    new(AnimationProperty.FILL, [new(new(1), new SceneColor(-2, 4, 8, 0.4), KeyframeInterpolation.EASE_IN)
                    {
                        ComponentCurves = [new(KeyframeInterpolation.EASE_OUT), null, new(KeyframeInterpolation.HOLD, 0.2, 0.8)]
                    }])
                ]
            }]
        };
        ProjectValidator.Validate(project);
        var element = JsonSerializer.SerializeToElement(project, ExportWire.Options);
        var job = new ExportWorkerJob(element, Path.GetTempPath(), Path.GetTempPath(), ".mkv", VideoCodec.H264,
            "medium", 18, AudioExportMode.None, 192000, "ffmpeg",
            RateControlMode: VideoRateControlMode.CRF, ProtocolVersion: ExportWire.VERSION);
        var encoded = JsonSerializer.Serialize(job, ExportWire.Options);
        var restored = Assert.IsType<ExportWorkerJob>(JsonSerializer.Deserialize<ExportWorkerJob>(encoded, ExportWire.Options));
        ExportWire.ValidateJob(restored);
        var layer = restored.Project.GetProperty("layers")[0];
        var position = layer.GetProperty("transform").GetProperty("position");
        Assert.Equal(20, position.GetProperty("x").GetDouble());
        Assert.Equal(-30, position.GetProperty("y").GetDouble());
        var vectorKey = layer.GetProperty("tracks")[0].GetProperty("keyframes")[0];
        var scalarKey = layer.GetProperty("tracks")[1].GetProperty("keyframes")[0];
        Assert.Equal(new ScenePoint(40, 50), vectorKey.GetProperty("value").Deserialize<AnimationValue>(ExportWire.Options).Vector);
        Assert.Equal(0.75, scalarKey.GetProperty("value").Deserialize<AnimationValue>(ExportWire.Options).Scalar);
        Assert.Equal(new AnimationCurve(KeyframeInterpolation.EASE_OUT, 0.2, 0.8),
            vectorKey.GetProperty("componentCurves")[0].Deserialize<AnimationCurve>(ExportWire.Options));
        var colorKey = layer.GetProperty("tracks")[2].GetProperty("keyframes")[0];
        Assert.Equal(new SceneColor(-2, 4, 8, 0.4), colorKey.GetProperty("value").Deserialize<AnimationValue>(ExportWire.Options).Color);
        Assert.Equal(3, colorKey.GetProperty("componentCurves").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, colorKey.GetProperty("componentCurves")[1].ValueKind);
        Assert.Equal(new AnimationCurve(KeyframeInterpolation.HOLD, 0.2, 0.8),
            colorKey.GetProperty("componentCurves")[2].Deserialize<AnimationCurve>(ExportWire.Options));
        Assert.Equal(1, vectorKey.GetProperty("time").GetProperty("numerator").GetInt64());
        Assert.Equal(JsonValueKind.Number, scalarKey.GetProperty("value").ValueKind);
        Assert.Equal(JsonValueKind.Object, vectorKey.GetProperty("value").ValueKind);
    }
}
