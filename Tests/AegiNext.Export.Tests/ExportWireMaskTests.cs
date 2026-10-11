using System.Text.Json;
using AegiNext.Application;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Export;

namespace AegiNext.Export.Tests;

public sealed class ExportWireMaskTests
{
    [Fact]
    public void WorkerRequestPreservesIndependentNodeTargetsPowerAndOrderedOperations()
    {
        var first = new MaskNode { Position = new(20, 30) };
        var second = new MaskNode { Position = new(100, 30) };
        var line = new SubtitleLine { Text = "animated" };
        var operation = new AnimationTransformOperation(Guid.NewGuid(), new(-1), new(1), new ScenePoint(30, 20), 0);
        var project = new ProjectDocument
        {
            Subtitles = [line],
            Layers = [new()
            {
                Id = line.Id, Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End,
                Mask = new VectorClipMask { Contours = [new() { Nodes = [first, second] }] },
                Tracks =
                [
                    new(new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, first.Id),
                        [new(new(0), first.Position, KeyframeInterpolation.POWER) { Exponent = 2 }, new(new(1), new ScenePoint(60, 30))]),
                    new(new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, second.Id),
                        [new(new(0), second.Position), new(new(1), new ScenePoint(120, 50))]),
                    new(AnimationProperty.MASK_POSITION, []) { InitialValue = new ScenePoint(0, 0), Transforms = [operation] }
                ]
            }]
        };
        ProjectValidator.Validate(project);
        var restored = ProjectStore.Deserialize(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(project, ExportWire.Options)));
        ProjectValidator.Validate(restored);

        Assert.Equal(project.Layers[0].Tracks.Select(track => track.Target), restored.Layers[0].Tracks.Select(track => track.Target));
        Assert.Equal(operation, restored.Layers[0].Tracks[2].Transforms[0]);
        Assert.Equal(2, restored.Layers[0].Tracks[0].Keyframes[0].Exponent);
        var evaluated = Assert.IsType<VectorClipMask>(SceneEvaluator.EvaluateMask(restored.Layers[0], new(1, 2)));
        Assert.Equal(new ScenePoint(30, 30), evaluated.Contours[0].Nodes[0].Position);
        Assert.Equal(new ScenePoint(110, 40), evaluated.Contours[0].Nodes[1].Position);
        Assert.Equal(new ScenePoint(30, 20), evaluated.Transform.Position);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WorkerRequestPreservesClipMaskAndCanonicalAnimationTarget(bool vector)
    {
        ClipMask mask = vector ? new VectorClipMask
        {
            Contours = [new() { Nodes = [new() { Position = new(20, 30), OutHandle = new(5, -4) }] }]
        } : new RectangleClipMask { TopLeft = new(20, 30), BottomRight = new(400, 300) };
        mask = mask with
        {
            Inverted = true,
            Transform = new() { Position = new(7, -9), Scale = new(2, 0.5), Rotation = 17, Pivot = new(200, 150) }
        };
        var line = new SubtitleLine { Text = "masked" };
        var track = new AnimationTrack(AnimationProperty.POSITION, [new(new(1, 3), new ScenePoint(40, 50))]);
        var project = new ProjectDocument
        {
            Subtitles = [line],
            Layers = [new()
            {
                Id = line.Id, Kind = LayerKind.SUBTITLE, SubtitleId = line.Id,
                Start = line.Start, End = line.End, Mask = mask, Tracks = [track]
            }],
            Presets = [new(Guid.NewGuid(), "position", [track])]
        };
        ProjectValidator.Validate(project);
        var job = new ExportWorkerJob(JsonSerializer.SerializeToElement(project, ExportWire.Options), Path.GetTempPath(),
            Path.GetTempPath(), ".mkv", VideoCodec.H264, "medium", 18, AudioExportMode.None, 192000, "ffmpeg",
            RateControlMode: VideoRateControlMode.CRF, ProtocolVersion: ExportWire.VERSION);
        var transmitted = JsonSerializer.Deserialize<ExportWorkerJob>(JsonSerializer.Serialize(job, ExportWire.Options), ExportWire.Options)!;
        ExportWire.ValidateJob(transmitted);
        var layer = transmitted.Project.GetProperty("layers")[0];
        var restoredMask = layer.GetProperty("mask").Deserialize<ClipMask>(ExportWire.Options)!;
        var wireTrack = layer.GetProperty("tracks")[0];

        Assert.Equal(ProjectDocument.CURRENT_VERSION, transmitted.Project.GetProperty("version").GetInt32());
        Assert.Equal(JsonSerializer.Serialize(mask, ExportWire.Options), JsonSerializer.Serialize(restoredMask, ExportWire.Options));
        Assert.Equal(vector ? "VECTOR" : "RECTANGLE", layer.GetProperty("mask").GetProperty("kind").GetString());
        Assert.Equal(track.Target, wireTrack.GetProperty("target").Deserialize<AnimationTrackTarget>(ExportWire.Options));
        Assert.False(wireTrack.TryGetProperty("property", out _));
        Assert.False(transmitted.Project.GetProperty("presets")[0].TryGetProperty("mask", out _));
        var time = wireTrack.GetProperty("keyframes")[0].GetProperty("time");
        Assert.Equal(1, time.GetProperty("numerator").GetInt64());
        Assert.Equal(3, time.GetProperty("denominator").GetInt64());
    }
}
