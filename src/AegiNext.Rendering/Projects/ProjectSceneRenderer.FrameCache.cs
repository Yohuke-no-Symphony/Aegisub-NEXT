using System.Collections.Immutable;
using System.Diagnostics;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Rendering.Images;

namespace AegiNext.Rendering.Projects;

public sealed partial class ProjectSceneRenderer
{
    private LinearRenderSurface? frameSurface;
    private ImmutableArray<EvaluatedLayer> frameLayers = [];
    private ImmutableArray<ImageFrameIdentity?> frameImageFrames = [];
    private bool frameValid;
    private ulong frameRevision;
    private ulong frameEvaluations;
    private ulong frameRedraws;
    private ulong frameCopies;
    private ulong frameCopiedBytes;
    private double frameEvaluateMilliseconds;
    private double frameDrawMilliseconds;
    private double frameCopyMilliseconds;
    private readonly bool profileFrameCache = Environment.GetEnvironmentVariable("AEGINEXT_EXPORT_PROFILE") == "1";

    /// <summary>返回累计求值、重绘及像素复制计数；阶段时间仅在导出性能诊断启用时采集。</summary>
    public ProjectFrameCacheStatistics FrameCacheStatistics => new(frameEvaluations, frameRedraws, frameCopies,
        frameCopiedBytes, frameEvaluateMilliseconds, frameDrawMilliseconds, frameCopyMilliseconds);

    /// <summary>
    /// 复用渲染器拥有的全尺寸 F16 帧，并始终完整复制预乘线性 float 像素；返回本次是否重新绘制。
    /// </summary>
    public bool CopyCachedFramePixels(ProjectDocument document, MediaTime time, Span<float> destination,
        CancellationToken cancellationToken = default)
    {
        UpdateFramePixels(document, time, destination, 0, true, cancellationToken, out var redrawn);
        return redrawn;
    }

    /// <summary>
    /// 仅在前景变化或目标未持有当前版本时完整写入像素；空前景不写入目标。
    /// 调用方必须保留目标内容并只在成功后记录返回版本，目标更换或失效时传入零。
    /// </summary>
    public CachedFramePixelUpdate UpdateCachedFramePixels(ProjectDocument document, MediaTime time,
        Span<float> destination, ulong destinationRevision, CancellationToken cancellationToken = default)
    {
        return UpdateFramePixels(document, time, destination, destinationRevision, false, cancellationToken, out _);
    }

    private CachedFramePixelUpdate UpdateFramePixels(ProjectDocument document, MediaTime time, Span<float> destination,
        ulong destinationRevision, bool alwaysCopy, CancellationToken cancellationToken, out bool redrawn)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(document);
        if (destination.Length < checked(document.Width * document.Height * 4))
        {
            throw new ArgumentException("目标缓冲不足以容纳全部像素。", nameof(destination));
        }

        cancellationToken.ThrowIfCancellationRequested();
        Prepare(document);
        var start = profileFrameCache ? Stopwatch.GetTimestamp() : 0;
        var layers = SceneEvaluator.Evaluate(prepared!, time);
        frameEvaluations++;
        if (profileFrameCache)
        {
            frameEvaluateMilliseconds += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }

        var imageFrames = ImageFrames(document, layers, time);
        redrawn = !frameValid || !Equivalent(frameLayers, layers) || !frameImageFrames.SequenceEqual(imageFrames);
        if (redrawn)
        {
            frameValid = false;
            start = profileFrameCache ? Stopwatch.GetTimestamp() : 0;
            if (!layers.IsEmpty)
            {
                frameSurface ??= new(new(document.Width, document.Height, (float)document.ReferenceWhiteNits), graphicsContext);
                frameSurface.Clear();
                foreach (var layer in layers)
                {
                    DrawLayer(document, frameSurface.Canvas, layer, time, cancellationToken: cancellationToken);
                }
            }

            frameRedraws++;
            if (profileFrameCache)
            {
                frameDrawMilliseconds += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        var updated = redrawn || destinationRevision != frameRevision;
        if (alwaysCopy || (updated && !layers.IsEmpty))
        {
            start = profileFrameCache ? Stopwatch.GetTimestamp() : 0;
            if (layers.IsEmpty)
            {
                destination[..checked(document.Width * document.Height * 4)].Clear();
            }
            else
            {
                frameSurface!.CopyPixels(destination);
            }

            frameCopies++;
            frameCopiedBytes += checked((ulong)document.Width * (ulong)document.Height * 4 * sizeof(float));
            if (profileFrameCache)
            {
                frameCopyMilliseconds += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (redrawn)
        {
            frameRevision = checked(frameRevision + 1);
            frameLayers = layers;
            frameImageFrames = imageFrames;
            frameValid = true;
        }

        return new(frameRevision, updated, layers.IsEmpty);
    }

    private void ClearFrameCache()
    {
        frameSurface?.Dispose();
        frameSurface = null;
        frameLayers = [];
        frameImageFrames = [];
        frameValid = false;
    }
}
