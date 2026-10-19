using System.Collections.Immutable;
using AegiNext.Core.Fonts;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Rendering.Projects;
using AegiNext.Rendering.Fonts;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace AegiNext.Export;

internal sealed class ExportRenderContext : IDisposable
{
    private readonly ProjectSceneRenderer renderer;
    private readonly int channelCount;
    private readonly IProgress<VideoExportProgress>? progress;
    private readonly CancellationToken cancellationToken;
    private readonly ProjectDocument project;
    private ulong frames;
    private ulong retainedRevision;
    private readonly bool profileEnabled = Environment.GetEnvironmentVariable("AEGINEXT_EXPORT_PROFILE") == "1";

    internal ExportRenderContext(ProjectDocument project, string projectDirectory, IProgress<VideoExportProgress>? progress,
        CancellationToken cancellationToken, ImmutableArray<FontFamilyBundle> fontFamilies = default)
    {
        this.project = project;
        this.progress = progress;
        this.cancellationToken = cancellationToken;
        renderer = new(new DirectoryProjectAssetResolver(projectDirectory),
            fontFamilies.IsDefaultOrEmpty ? null : new SystemFontCatalog(fontFamilies));
        channelCount = checked(project.Width * project.Height * 4);
    }

    internal Exception? Failure { get; private set; }
    internal nint NativeContext { get; set; }

    internal unsafe int Render(long pts, int timeBaseNumerator, int timeBaseDenominator, uint width, uint height,
        float* output, ulong channels, NativeExportOverlayInfo* overlay)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (width != project.Width || height != project.Height || channels != (ulong)channelCount || output is null)
            {
                throw new InvalidDataException("The native export frame dimensions do not match the project.");
            }

            if (overlay is null || overlay->StructSize != NativeExportAbi.OVERLAY_SIZE ||
                overlay->AbiVersion != NativeExportAbi.VERSION || overlay->Reserved != 0 ||
                overlay->State != 0 || overlay->Revision != 0)
            {
                throw new InvalidDataException("The native export overlay callback protocol does not match.");
            }

            var time = new MediaTimestamp(pts, new(timeBaseNumerator, timeBaseDenominator)).ToMediaTime() - project.Media!.MediaOrigin;
            var update = renderer.UpdateCachedFramePixels(project, time, new Span<float>(output, channelCount),
                retainedRevision, cancellationToken);

            frames++;
            if (progress is not null && (frames == 1 || frames % 10 == 0))
            {
                var encoder = Marshal.PtrToStringUTF8(NativeExportMethods.EncoderName(NativeContext));
                progress?.Report(new(frames, time, null, "encoding", encoder));
            }

            cancellationToken.ThrowIfCancellationRequested();
            overlay->State = update.Updated
                ? update.Empty ? NativeExportOverlayState.EMPTY : NativeExportOverlayState.UPDATED
                : NativeExportOverlayState.UNCHANGED;
            overlay->Revision = update.Revision;
            retainedRevision = update.Revision;

            return 0;
        }
        catch (OperationCanceledException)
        {
            retainedRevision = 0;
            return 1;
        }
        catch (Exception exception)
        {
            retainedRevision = 0;
            Failure = exception;
            return 2;
        }
    }

    public void Dispose()
    {
        if (profileEnabled)
        {
            var stats = renderer.FrameCacheStatistics;
            var surfaces = renderer.RenderSurfaceStatistics;
            Console.Error.WriteLine("AEGINEXT_RENDER_PROFILE " + JsonSerializer.Serialize(new
            {
                frames, evaluations = stats.Evaluations, redraws = stats.Redraws, copies = stats.Copies,
                copied_bytes = stats.CopiedBytes, evaluate_ms = stats.EvaluateMilliseconds,
                draw_ms = stats.DrawMilliseconds, copy_ms = stats.CopyMilliseconds,
                surface_allocations = surfaces.Allocations, surface_reuses = surfaces.Reuses,
                surface_releases = surfaces.ReleasedSurfaces, surface_retained_bytes = surfaces.RetainedBytes,
                surface_peak_active = surfaces.PeakActiveLeases
            }));
        }

        renderer.Dispose();
    }
}
