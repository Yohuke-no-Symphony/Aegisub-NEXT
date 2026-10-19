using System.Security.Cryptography;
using AegiNext.Core.Fonts;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Export;

namespace AegiNext.Export.Tests;

public sealed class ExportRenderContextTests
{
    [Fact]
    public void InvalidApplicationFontSnapshotIsRejectedBeforeRendering()
    {
        byte[] bytes = [1, 2, 3, 4];
        var family = new FontFamilyBundle("Private Family", [new("private.ttf",
            Convert.ToHexStringLower(SHA256.HashData(bytes)), [.. bytes])]);

        Assert.Throws<InvalidDataException>(() =>
        {
            using var context = new ExportRenderContext(Project(), AppContext.BaseDirectory, null, CancellationToken.None, [family]);
        });
    }

    [Fact]
    public unsafe void CallbackPublishesUpdatesAndRetainsTheNativeBufferOnHits()
    {
        var project = Project();
        using var context = new ExportRenderContext(project, AppContext.BaseDirectory, null, CancellationToken.None);
        var pixels = new float[project.Width * project.Height * 4];
        fixed (float* output = pixels)
        {
            var info = OverlayInfo();
            Assert.Equal(0, context.Render(0, 1, 30, 32, 32, output, (ulong)pixels.Length, &info));
            Assert.Equal(NativeExportOverlayState.UPDATED, info.State);
            Assert.NotEqual(0UL, info.Revision);
            var revision = info.Revision;
            Assert.Contains(pixels, value => value != 0);
            pixels.AsSpan().Fill(float.NaN);
            info = OverlayInfo();
            Assert.Equal(0, context.Render(1, 1, 30, 32, 32, output, (ulong)pixels.Length, &info));
            Assert.Equal(NativeExportOverlayState.UNCHANGED, info.State);
            Assert.Equal(revision, info.Revision);
            Assert.All(pixels, value => Assert.True(float.IsNaN(value)));

            info = OverlayInfo();
            Assert.Equal(0, context.Render(60, 1, 30, 32, 32, output, (ulong)pixels.Length, &info));
            Assert.Equal(NativeExportOverlayState.EMPTY, info.State);
            Assert.True(info.Revision > revision);
            Assert.All(pixels, value => Assert.True(float.IsNaN(value)));
        }

        Assert.Null(context.Failure);
    }

    [Theory]
    [InlineData(0U, 5U, 0U, 0U, 0UL)]
    [InlineData(24U, 4U, 0U, 0U, 0UL)]
    [InlineData(24U, 5U, 1U, 0U, 0UL)]
    [InlineData(24U, 5U, 0U, 1U, 0UL)]
    [InlineData(24U, 5U, 0U, 0U, 1UL)]
    public unsafe void InvalidCallbackMetadataFailsBeforeWriting(uint size, uint version, uint state, uint reserved, ulong revision)
    {
        using var context = new ExportRenderContext(Project(), AppContext.BaseDirectory, null, CancellationToken.None);
        var pixels = new float[32 * 32 * 4];
        pixels.AsSpan().Fill(42);
        var info = new NativeExportOverlayInfo
        {
            StructSize = size, AbiVersion = version, State = (NativeExportOverlayState)state,
            Reserved = reserved, Revision = revision
        };
        fixed (float* output = pixels)
        {
            Assert.Equal(2, context.Render(0, 1, 30, 32, 32, output, (ulong)pixels.Length, &info));
        }

        Assert.IsType<InvalidDataException>(context.Failure);
        Assert.All(pixels, value => Assert.Equal(42, value));
    }

    [Fact]
    public unsafe void CancellationCannotPublishARevision()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var context = new ExportRenderContext(Project(), AppContext.BaseDirectory, null, cancellation.Token);
        var pixels = new float[32 * 32 * 4];
        var info = OverlayInfo();
        fixed (float* output = pixels)
        {
            Assert.Equal(1, context.Render(0, 1, 30, 32, 32, output, (ulong)pixels.Length, &info));
        }

        Assert.Equal(0UL, info.Revision);
        Assert.Equal((NativeExportOverlayState)0, info.State);
        Assert.Null(context.Failure);
    }

    private static NativeExportOverlayInfo OverlayInfo() => new()
    {
        StructSize = NativeExportAbi.OVERLAY_SIZE, AbiVersion = NativeExportAbi.VERSION
    };

    private static ProjectDocument Project()
    {
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, "unused.mp4");
        return new()
        {
            Width = 32, Height = 32, Assets = [asset], Media = new(asset.Id, 0, null, MediaTime.Zero),
            Layers = [new()
            {
                Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 16, 16),
                Fill = new(4, 2, 0, 0.5), End = new(2)
            }]
        };
    }
}
