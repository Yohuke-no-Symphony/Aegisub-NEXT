using System.Text.Json;
using AegiNext.Application;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Export.Worker;
using AegiNext.Rendering.Projects;

namespace AegiNext.Export.Tests;

public sealed class UnifiedEffectStackExportTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public unsafe void WorkerWireAndNativeOverlayCallbackSharePreviewCompositionForEachFrame(bool nativeAbove)
    {
        var native = new AnimationTrack(AnimationProperty.POSITION,
            [new(new(0), new ScenePoint(8, 8)), new(new(1), new ScenePoint(16, 8))]);
        var instance = new EffectInstance { Name = "Export offset", End = new(1), Source = """
            effect "export-stack" version 3
            short-clip compress
            scope whole current
                segment all flex 1
                    at 0 position offset(8, 0)
                    at 1 position offset(8, 0)
                end
            end
            """ };
        TimelineEffectRowIdentity manual = new(native.Target);
        TimelineEffectRowIdentity script = new(InstanceId: instance.Id);
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, "unused.mp4");
        var layer = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 12, 12), End = new(1),
            Fill = new(1, 0.5, 0.25, 0.5), Tracks = [native], Effects = [instance],
            EffectRowOrder = nativeAbove ? [manual, script] : [script, manual] };
        var project = new ProjectDocument { Width = 64, Height = 64, Assets = [asset],
            Media = new(asset.Id, 0, null, MediaTime.Zero), Layers = [layer] };
        var job = new ExportWorkerJob(JsonSerializer.SerializeToElement(project, ExportWire.Options),
            AppContext.BaseDirectory, AppContext.BaseDirectory, ".mkv", "ffmpeg", ExportWorkerOperation.BURN_IN,
            new(VideoCodec.H264, "medium", 18, AudioExportMode.None, 192000, RateControlMode: VideoRateControlMode.CRF),
            ProtocolVersion: ExportWire.VERSION);
        var received = Assert.IsType<ExportWorkerJob>(JsonSerializer.Deserialize<ExportWorkerJob>(
            JsonSerializer.Serialize(job, ExportWire.Options), ExportWire.Options));
        ExportWire.ValidateJob(received);
        var restored = ProjectStore.Deserialize(received.Project);
        Assert.Equal<TimelineEffectRowIdentity>(layer.EffectRowOrder, restored.Layers[0].EffectRowOrder);
        Assert.Equal(nativeAbove ? 10 : 18,
            SceneEvaluator.EvaluateLayer(new PreparedProjectScene(restored), layer.Id, new(1, 4)).Transform.Position.X);
        using var preview = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(AppContext.BaseDirectory));
        using var export = new ExportRenderContext(restored, AppContext.BaseDirectory, null, CancellationToken.None);
        var actual = new float[project.Width * project.Height * 4];
        var expected = new float[actual.Length];
        foreach (var numerator in new long[] { 1, 3 })
        {
            preview.CopyCachedFramePixels(project, new(numerator, 4), expected);
            var overlay = new NativeExportOverlayInfo { StructSize = NativeExportAbi.OVERLAY_SIZE, AbiVersion = NativeExportAbi.VERSION };
            fixed (float* output = actual)
            {
                Assert.Equal(0, export.Render(numerator, 1, 4, (uint)project.Width, (uint)project.Height,
                    output, (ulong)actual.Length, &overlay));
            }
            Assert.Null(export.Failure);
            Assert.Equal(NativeExportOverlayState.UPDATED, overlay.State);
            Assert.Contains(expected, value => value > 0);
            Assert.Equal(expected, actual);
        }
    }
}
