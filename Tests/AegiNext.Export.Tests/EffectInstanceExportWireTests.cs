using System.Collections.Immutable;
using System.Text.Json;
using AegiNext.Application;
using AegiNext.Core.Editing;
using AegiNext.Core.Effects;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Export.Worker;

namespace AegiNext.Export.Tests;

public sealed class EffectInstanceExportWireTests
{
    [Fact]
    public void WorkerRequestPreservesParameterizedInstancesAndEvaluatesTheSameNonlinearStack()
    {
        const string SOURCE = """
            effect "worker-instance" version 3
            short-clip compress
            var strength = 2 options ["Normal":2, "Strong":3]
            scope whole current
                segment hold flex 1
                    at 0 scale factor(strength, strength)
                    at 1 scale factor(strength, strength)
                end
            end
            """;
        var instance = new EffectInstance
        {
            Name = "Worker snapshot", Source = SOURCE, Start = new(1, 3), End = new(4, 3),
            Parameters = ImmutableDictionary<string, EffectScriptParameterValue>.Empty
                .Add("strength", EffectScriptParameterValue.FromNumber(3))
        };
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 100, 60), End = new(2),
            Tracks = [new(AnimationProperty.SCALE,
            [
                new(MediaTime.Zero, new ScenePoint(0, 0), KeyframeInterpolation.POWER) { Exponent = 2 },
                new(new(1), new ScenePoint(1, 1))
            ])],
            Effects = [instance]
        };
        var document = new ProjectDocument { Layers = [layer] };
        var job = new ExportWorkerJob(JsonSerializer.SerializeToElement(document, ExportWire.Options),
            Path.GetTempPath(), Path.GetTempPath(), ".mkv", "ffmpeg", ExportWorkerOperation.BURN_IN,
            new(VideoCodec.H264, "medium", 18, AudioExportMode.None, 192000, RateControlMode: VideoRateControlMode.CRF),
            ProtocolVersion: ExportWire.VERSION);

        var received = Assert.IsType<ExportWorkerJob>(JsonSerializer.Deserialize<ExportWorkerJob>(
            JsonSerializer.Serialize(job, ExportWire.Options), ExportWire.Options));
        ExportWire.ValidateJob(received);
        var restored = ProjectStore.Deserialize(received.Project);
        var restoredLayer = Assert.Single(restored.Layers);
        var restoredInstance = Assert.Single(restoredLayer.Effects);
        var scene = new PreparedProjectScene(restored);

        Assert.Equal(instance.Id, restoredInstance.Id);
        Assert.Equal(SOURCE, restoredInstance.Source);
        Assert.Equal(instance.Start, restoredInstance.Start);
        Assert.Equal(instance.End, restoredInstance.End);
        Assert.Equal(instance.Parameters["strength"], restoredInstance.Parameters["strength"]);
        Assert.Single(restoredLayer.Tracks);
        Assert.Equal(new ScenePoint(0.75, 0.75), SceneEvaluator.EvaluateLayer(scene, layer.Id, new(1, 2)).Transform.Scale);
        Assert.Equal(new ScenePoint(0.0625, 0.0625), SceneEvaluator.EvaluateLayer(scene, layer.Id, new(1, 4)).Transform.Scale);
    }
}
