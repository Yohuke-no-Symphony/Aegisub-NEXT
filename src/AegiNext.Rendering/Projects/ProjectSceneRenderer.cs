using System.Collections.Immutable;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Rendering.Fonts;
using AegiNext.Rendering.Images;
using SkiaSharp;

namespace AegiNext.Rendering.Projects;

/// <summary>共享线性 F16 工程渲染逻辑；默认 CPU，也可借用调用方的 GPU 上下文。实例及缓存限同一线程使用。</summary>
public sealed partial class ProjectSceneRenderer : IDisposable
{
    private const float PREVIEW_REFERENCE_WHITE_NITS = 203;
    private readonly IProjectAssetResolver assets;
    private readonly GRContext? graphicsContext;
    private readonly ImageFrameCache images;
    private readonly bool skipUnavailableImagesInPreview;
    private readonly Dictionary<Guid, ProjectImageRenderException> previewImageFailures = [];
    private ImmutableArray<ImageFrameIdentity?> previewImageFrames = [];
    private readonly SubtitleLayoutCache layouts = new();
    private readonly Dictionary<(Guid? Asset, string Family, SubtitleFontVariant? Variant, bool Bold, bool Italic), TextShaper> textShapers = [];
    private readonly Dictionary<(nint Handle, SubtitleFontVariant? Variant, int Instance, int Collection, bool Bold, bool Italic), TextShaper> actualTextShapers = [];
    private readonly Dictionary<(string Family, SubtitleFontVariant? Variant, bool Bold, bool Italic, string Grapheme), TextShaper> resolvedTextShapers = [];
    private readonly Lazy<SystemFontResolver> systemFonts;
    private readonly SKColorSpace linear = SKColorSpace.CreateSrgbLinear();
    private readonly RenderSurfacePool surfacePool;
    private PreparedProjectScene? prepared;
    private bool isDisposed;
    private LinearRenderSurface? previewScene;
    private LinearRenderSurface? previewTarget;
    private ImmutableArray<EvaluatedLayer> previewLayers = [];
    private ImmutableArray<EvaluatedLayer> evaluatedPreviewLayers = [];
    private MediaTime? evaluatedPreviewTime;
    private bool previewSceneValid;
    private SKBlender? additiveBlend;
    private SKColorFilter? previewWhiteFilter;
    private GpuLinearColorShader? extendedColorShader;

    /// <summary>绑定资源解析器、字体目录和可选 GPU 上下文；不接管借用资源生命周期。GPU 调用和释放须在创建线程的当前上下文中执行。</summary>
    public ProjectSceneRenderer(IProjectAssetResolver assets, SystemFontCatalog? fontCatalog = null, GRContext? graphicsContext = null,
        bool skipUnavailableImagesInPreview = false)
        : this(assets, fontCatalog, RenderSurfacePool.DEFAULT_RETAINED_BYTES, graphicsContext)
    {
        this.skipUnavailableImagesInPreview = skipUnavailableImagesInPreview;
    }

    internal ProjectSceneRenderer(IProjectAssetResolver assets, SystemFontCatalog? fontCatalog, long maximumRetainedSurfaceBytes,
        GRContext? graphicsContext = null)
    {
        ArgumentNullException.ThrowIfNull(assets);
        this.assets = assets;
        images = new(assets);
        this.graphicsContext = graphicsContext;
        surfacePool = new(maximumRetainedSurfaceBytes, graphicsContext);
        systemFonts = new(() => new(fontCatalog ?? new SystemFontCatalog()));
    }

    /// <summary>返回渲染器拥有的临时表面池计数；不包含交给调用方或单独帧缓存持有的表面。</summary>
    public RenderSurfacePoolStatistics RenderSurfaceStatistics => surfacePool.Statistics;
    internal int CachedSubtitleLayoutCount => layouts.Count;
    internal int CachedTextShaperCount => actualTextShapers.Count;
    internal ulong DecodedImageFrameCount => images.DecodedFrames;
    /// <summary>最近预览中被明确隔离的图片错误；完整渲染和导出始终严格失败。</summary>
    public IReadOnlyDictionary<Guid, ProjectImageRenderException> PreviewImageFailures => previewImageFailures;

    /// <summary>在工程精确时间渲染透明的预乘 F16 字幕与图形层，调用方负责释放结果。</summary>
    public LinearRenderSurface Render(ProjectDocument document, MediaTime time)
    {
        ArgumentNullException.ThrowIfNull(document);
        var surface = new LinearRenderSurface(new(document.Width, document.Height, (float)document.ReferenceWhiteNits), graphicsContext);
        try
        {
            RenderInto(document, time, surface);
            return surface;
        }
        catch
        {
            surface.Dispose();
            throw;
        }
    }

    /// <summary>清空并复用匹配工程尺寸和参考白的表面；效果与分组始终在线性光中合成。</summary>
    public void RenderInto(ProjectDocument document, MediaTime time, LinearRenderSurface destination)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(destination);
        if (destination.Info.Width != document.Width || destination.Info.Height != document.Height ||
            !destination.Info.ReferenceWhiteNits.Equals((float)document.ReferenceWhiteNits))
        {
            throw new ArgumentException("The render surface must match the project dimensions and reference white.", nameof(destination));
        }

        Prepare(document);
        var layers = SceneEvaluator.Evaluate(prepared!, time);
        destination.Clear();
        foreach (var layer in layers)
        {
            DrawLayer(document, destination.Canvas, layer, time);
        }
    }

    /// <summary>返回当前可见图层与实际渲染一致的字形边界及变换；找不到或图层在当前时间不可见时返回 null。</summary>
    public ProjectLayerGeometry? GetLayerGeometry(ProjectDocument document, MediaTime time, Guid layerId)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(document);
        Prepare(document);
        return FindGeometry(document, SceneEvaluator.Evaluate(prepared!, time), layerId, SKMatrix.Identity);
    }

    /// <summary>按实际绘制顺序从前向后命中可见轮廓，每次点击只求值场景一次。</summary>
    public Guid? HitTestLayer(ProjectDocument document, MediaTime time, SKPoint point)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(document);
        Prepare(document);
        var layers = SceneEvaluator.Evaluate(prepared!, time);
        for (var index = layers.Length - 1; index >= 0; index--)
        {
            var layer = layers[index];
            if (layer.Opacity > 0 && Geometry(document, layer, SKMatrix.Identity) is { HasInk: true } geometry &&
                geometry.ContainsWorldPoint(point))
            {
                return layer.Source.Id;
            }
        }
        return null;
    }

    /// <summary>将自动对齐转换为保持实际字形位置的显式锚点，不要求字幕在当前播放时间可见。</summary>
    public SubtitlePosition ResolveSubtitlePosition(ProjectDocument document, SubtitleLine subtitle)
    {
        return SubtitleStyleScaling.ToReferencePosition(MeasureSubtitlePlacement(document, subtitle).Position,
            subtitle.Style.Scaling, document.Width, document.Height);
    }

    /// <summary>测量任意字幕的真实字形及自动布局锚点，不依赖当前播放时间或图层可见性。</summary>
    public SubtitlePlacementMeasurement MeasureSubtitlePlacement(ProjectDocument document, SubtitleLine subtitle)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(subtitle);
        Prepare(document);
        subtitle = CanvasSubtitle(document, subtitle);
        var layout = Layout(document, subtitle);
        return Placement(subtitle, layout, document.Width, document.Height);
    }

    /// <summary>测量已经求值的字幕，当前字距动画参与边界与轴心计算。</summary>
    public SubtitlePlacementMeasurement MeasureSubtitlePlacement(ProjectDocument document, EvaluatedLayer layer)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(layer);
        if (layer.Subtitle is null)
        {
            throw new ArgumentException("Layout measurement requires a subtitle layer.", nameof(layer));
        }
        Prepare(document);
        layer = CanvasLayer(document, layer);
        return Placement(layer.Subtitle!, Layout(document, layer), document.Width, document.Height);
    }

    private static SubtitlePlacementMeasurement Placement(SubtitleLine subtitle, SubtitleLayout layout, int width, int height)
    {
        var resolved = subtitle.Style.Position ?? SubtitlePosition.FromAlignment(subtitle.Style.Alignment, subtitle.Style.Margins);
        if (subtitle.Style.Position is null)
        {
            resolved = resolved with
            {
                Offset = new(layout.BasePosition.X - resolved.Anchor.X * width,
                    layout.BasePosition.Y - resolved.Anchor.Y * height)
            };
        }

        return new(resolved, layout.Bounds, layout.HasInk);
    }

    /// <summary>将工程叠层重标到 203 nit 名义白，与 sRGB BGRA 背景在线性光合成；只缩放叠层 RGB，不改变 alpha 或导出表面。</summary>
    public byte[] ComposePreview(ProjectDocument document, MediaTime time, ReadOnlySpan<byte> bgra, int width, int height, int rowBytes)
    {
        return ComposePreviewCore(document, time, bgra, width, height, rowBytes, width, height, false);
    }

    /// <summary>将视频等比例放入指定的工程预览表面，保持工程叠层的坐标与宽高比，并使用 203 nit SDR 名义白。</summary>
    public byte[] ComposePreview(ProjectDocument document, MediaTime time, ReadOnlySpan<byte> bgra,
        int width, int height, int rowBytes, int outputWidth, int outputHeight, CancellationToken cancellationToken = default)
    {
        return ComposePreviewCore(document, time, bgra, width, height, rowBytes, outputWidth, outputHeight, true, cancellationToken);
    }

    /// <summary>查询已验证快照在给定时间是否包含叠层，供视频预览直接复用匹配尺寸的背景。</summary>
    public bool HasPreviewLayers(ProjectDocument document, MediaTime time)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(document);
        Prepare(document);
        return !EvaluatePreview(time).IsEmpty;
    }

    private byte[] ComposePreviewCore(ProjectDocument document, MediaTime time, ReadOnlySpan<byte> bgra,
        int width, int height, int rowBytes, int outputWidth, int outputHeight, bool projectViewport, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(document);
        if (width <= 0 || height <= 0 || rowBytes < checked(width * 4) || bgra.Length < checked(rowBytes * height))
        {
            throw new ArgumentException("Invalid preview buffer.", nameof(bgra));
        }

        if (outputWidth is <= 0 or > 32768 || outputHeight is <= 0 or > 32768 ||
            (long)outputWidth * outputHeight > 33177600)
        {
            throw new ArgumentOutOfRangeException(nameof(outputWidth), "The preview surface exceeds the pixel budget.");
        }

        Prepare(document);
        cancellationToken.ThrowIfCancellationRequested();
        var layers = EvaluatePreview(time);
        var visibleImages = layers.Where(layer => layer.Source.Image is not null).Select(layer => layer.Source.Image!.AssetId).ToHashSet();
        foreach (var id in previewImageFailures.Keys.Where(id => !visibleImages.Contains(id)).ToArray())
        {
            previewImageFailures.Remove(id);
        }
        if (layers.IsEmpty && width == outputWidth && height == outputHeight && IsOpaqueBackground(bgra, width, height, rowBytes))
        {
            var pixels = new byte[checked(outputWidth * outputHeight * 4)];
            for (var row = 0; row < height; row++)
            {
                bgra.Slice(row * rowBytes, width * 4).CopyTo(pixels.AsSpan(row * width * 4, width * 4));
            }
            return pixels;
        }
        var scale = Math.Min(1, Math.Min((double)outputWidth / document.Width, (double)outputHeight / document.Height));
        var sceneWidth = Math.Max(1, (int)Math.Round(document.Width * scale));
        var sceneHeight = Math.Max(1, (int)Math.Round(document.Height * scale));
        if (previewScene is null || previewScene.Info.Width != sceneWidth || previewScene.Info.Height != sceneHeight)
        {
            previewScene?.Dispose();
            previewScene = new(new(sceneWidth, sceneHeight, (float)document.ReferenceWhiteNits), graphicsContext);
            previewSceneValid = false;
        }
        if (previewTarget is null || previewTarget.Info.Width != outputWidth || previewTarget.Info.Height != outputHeight)
        {
            previewTarget?.Dispose();
            previewTarget = new(new(outputWidth, outputHeight, PREVIEW_REFERENCE_WHITE_NITS), graphicsContext);
        }

        var imageFrames = ImageFrames(document, layers, time, skipUnavailableImagesInPreview);
        if (!previewSceneValid || !Equivalent(previewLayers, layers) || !previewImageFrames.SequenceEqual(imageFrames))
        {
            cancellationToken.ThrowIfCancellationRequested();
            previewSceneValid = false;
            previewScene.Clear();
            previewScene.Canvas.SetMatrix(SKMatrix.CreateScale((float)sceneWidth / document.Width, (float)sceneHeight / document.Height));
            foreach (var evaluated in layers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DrawLayer(document, previewScene.Canvas, evaluated, time, sceneWidth, sceneHeight, (float)scale,
                    skipUnavailableImagesInPreview, cancellationToken);
            }

            previewLayers = layers;
            previewImageFrames = imageFrames;
            previewSceneValid = true;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var scene = previewScene;
        var result = previewTarget;
        using var srgb = SKColorSpace.CreateSrgb();
        using var background = SKImage.FromPixelCopy(new(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque, srgb), bgra, rowBytes)
            ?? throw new InvalidOperationException("The video preview could not be read.");
        result.Canvas.Clear(SKColors.Black);
        var output = new SKRect(0, 0, outputWidth, outputHeight);
        var videoBounds = projectViewport ? FitPreview(width, height, outputWidth, outputHeight) : output;
        result.Canvas.DrawImage(background, videoBounds, new SKSamplingOptions(SKFilterMode.Linear));
        using var layer = scene.Snapshot();
        if (!document.ReferenceWhiteNits.Equals(PREVIEW_REFERENCE_WHITE_NITS))
        {
            previewWhiteFilter ??= CreatePreviewWhiteFilter((float)(document.ReferenceWhiteNits / PREVIEW_REFERENCE_WHITE_NITS));
        }

        using var layerPaint = new SKPaint();
        layerPaint.ColorFilter = previewWhiteFilter;
        var sceneBounds = projectViewport ? FitPreview(document.Width, document.Height, outputWidth, outputHeight) : output;
        result.Canvas.DrawImage(layer, sceneBounds, new SKSamplingOptions(SKFilterMode.Linear), layerPaint);
        return result.CopySrgbBgra();
    }

    private static SKRect FitPreview(int width, int height, int outputWidth, int outputHeight)
    {
        var scale = Math.Min((double)outputWidth / width, (double)outputHeight / height);
        var fittedWidth = (float)(width * scale);
        var fittedHeight = (float)(height * scale);
        var left = (outputWidth - fittedWidth) / 2;
        var top = (outputHeight - fittedHeight) / 2;
        return new(left, top, left + fittedWidth, top + fittedHeight);
    }

    private static bool IsOpaqueBackground(ReadOnlySpan<byte> bgra, int width, int height, int rowBytes)
    {
        for (var row = 0; row < height; row++)
        {
            for (var column = 0; column < width; column++)
            {
                if (bgra[row * rowBytes + column * 4 + 3] != byte.MaxValue)
                {
                    return false;
                }
            }
        }
        return true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        images.Dispose();

        ClearLayouts();
        ClearPreview();
        ClearFrameCache();
        surfacePool.Dispose();
        additiveBlend?.Dispose();
        extendedColorShader?.Dispose();
        linear.Dispose();
        isDisposed = true;
    }

    private void Prepare(ProjectDocument document)
    {
        if (!ReferenceEquals(prepared?.Document, document))
        {
            prepared = new(document);
            ClearPreview();
            ClearFrameCache();
            ClearLayouts();
        }

    }

    private void ClearPreview()
    {
        previewScene?.Dispose();
        previewTarget?.Dispose();
        previewWhiteFilter?.Dispose();
        previewWhiteFilter = null;
        previewScene = null;
        previewTarget = null;
        previewLayers = [];
        previewImageFrames = [];
        evaluatedPreviewLayers = [];
        evaluatedPreviewTime = null;
        previewSceneValid = false;
        previewImageFailures.Clear();
    }

    private ImmutableArray<EvaluatedLayer> EvaluatePreview(MediaTime time)
    {
        if (evaluatedPreviewTime != time)
        {
            var layers = SceneEvaluator.Evaluate(prepared!, time);
            evaluatedPreviewLayers = layers;
            evaluatedPreviewTime = time;
        }
        return evaluatedPreviewLayers;
    }

    private static bool Equivalent(ImmutableArray<EvaluatedLayer> previous, ImmutableArray<EvaluatedLayer> current)
    {
        if (previous.Length != current.Length)
        {
            return false;
        }

        for (var i = 0; i < previous.Length; i++)
        {
            var a = previous[i];
            var b = current[i];
            if (!ReferenceEquals(a.Source, b.Source) || !ReferenceEquals(a.Subtitle, b.Subtitle) ||
                a.HasFillAnimation != b.HasFillAnimation || a.HasStrokeAnimation != b.HasStrokeAnimation ||
                a.HasStrokeWidthAnimation != b.HasStrokeWidthAnimation || a.Transform != b.Transform || !a.Opacity.Equals(b.Opacity) ||
                a.HasLetterSpacingAnimation != b.HasLetterSpacingAnimation || !a.LetterSpacing.Equals(b.LetterSpacing) ||
                a.HasFillBlurAnimation != b.HasFillBlurAnimation || !a.FillBlur.Equals(b.FillBlur) ||
                a.HasStrokeBlurAnimation != b.HasStrokeBlurAnimation || !a.StrokeBlur.Equals(b.StrokeBlur) ||
                a.AnimationValues.Count != b.AnimationValues.Count ||
                a.AnimationValues.Any(pair => !b.AnimationValues.TryGetValue(pair.Key, out var value) || value != pair.Value) ||
                !a.AnimationRanges.SequenceEqual(b.AnimationRanges) ||
                a.Fill != b.Fill || a.Stroke != b.Stroke || !a.StrokeWidth.Equals(b.StrokeWidth) || !a.Blur.Equals(b.Blur) || !EquivalentMask(a.Mask, b.Mask) ||
                (a.Subtitle is { Karaoke.IsEmpty: false } && a.LocalTime != b.LocalTime))
            {
                return false;
            }
        }

        return true;
    }

    private void DrawLayer(ProjectDocument document, SKCanvas parent, EvaluatedLayer layer, MediaTime projectTime, int renderWidth = 0,
        int renderHeight = 0, float blurScale = 1, bool tolerateImageFailure = false, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var lease = surfacePool.Rent(new(renderWidth > 0 ? renderWidth : document.Width,
            renderHeight > 0 ? renderHeight : document.Height, (float)document.ReferenceWhiteNits));
        var surface = lease.Surface;
        var canvas = surface.Canvas;
        canvas.SetMatrix(parent.TotalMatrix);
        var saved = canvas.Save();
        try
        {
            var geometry = Geometry(document, layer, parent.TotalMatrix);
            canvas.SetMatrix(geometry.LocalToWorld);
            using var blend = Paint(new(1, 1, 1, layer.Opacity));
            blend.BlendMode = layer.Source.Blend switch
            {
                BlendMode.NORMAL => SKBlendMode.SrcOver,
                BlendMode.MULTIPLY => SKBlendMode.Multiply,
                BlendMode.SCREEN => SKBlendMode.Screen,
                BlendMode.ADD => SKBlendMode.Plus,
                BlendMode.OVERLAY => SKBlendMode.Overlay,
                BlendMode.DARKEN => SKBlendMode.Darken,
                BlendMode.LIGHTEN => SKBlendMode.Lighten,
                BlendMode.DIFFERENCE => SKBlendMode.Difference,
                _ => throw new InvalidDataException("Unknown layer blend mode.")
            };
            if (layer.Source.Blend == BlendMode.ADD)
            {
                additiveBlend ??= CreateAdditiveBlend();
                blend.Blender = additiveBlend;
            }

            switch (layer.Source.Kind)
            {
                case LayerKind.SUBTITLE:
                    DrawSubtitle(document, canvas, layer);
                    break;
                case LayerKind.SHAPE:
                    DrawShape(canvas, layer);
                    break;
                case LayerKind.IMAGE:
                    try
                    {
                        DrawImage(document, canvas, layer.Source, projectTime);
                        previewImageFailures.Remove(layer.Source.Image!.AssetId);
                    }
                    catch (ProjectImageRenderException error) when (tolerateImageFailure)
                    {
                        previewImageFailures[error.AssetId] = error;
                    }
                    break;
                default:
                    throw new InvalidDataException("Unknown layer kind.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            F16LayerBlur.Apply(surface, layer.Blur * blurScale);
            using var snapshot = surface.Snapshot();
            var parentSave = parent.Save();
            try
            {
                parent.ResetMatrix();
                if (layer.Mask is { } mask)
                {
                    using var path = ClipMaskPath(mask, (float)surface.Info.Width / document.Width,
                        (float)surface.Info.Height / document.Height);
                    parent.ClipPath(path, mask.Inverted ? SKClipOperation.Difference : SKClipOperation.Intersect, true);
                }

                parent.DrawImage(snapshot, 0, 0, blend);
            }
            finally
            {
                parent.RestoreToCount(parentSave);
            }
        }
        finally
        {
            canvas.RestoreToCount(saved);
        }
    }

    private void DrawShape(SKCanvas canvas, EvaluatedLayer layer)
    {
        var geometry = ShapeGeometry.CreatePath(layer.Source.Shape!);
        using var path = Path(geometry);
        if (geometry.Closed)
        {
            using var fill = Paint(layer.Fill);
            canvas.DrawPath(path, fill);
        }
        if (layer.StrokeWidth > 0)
        {
            using var stroke = Paint(layer.Stroke);
            stroke.Style = SKPaintStyle.Stroke;
            stroke.StrokeWidth = (float)layer.StrokeWidth;
            stroke.StrokeJoin = SKStrokeJoin.Round;
            canvas.DrawPath(path, stroke);
        }
    }

    private ImmutableArray<ImageFrameIdentity?> ImageFrames(ProjectDocument document, ImmutableArray<EvaluatedLayer> layers,
        MediaTime time, bool tolerateImageFailure = false)
    {
        var result = ImmutableArray.CreateBuilder<ImageFrameIdentity?>(layers.Length);
        foreach (var layer in layers)
        {
            try
            {
                result.Add(layer.Source.Image is null ? null : ImageFrame(document, layer.Source, time));
            }
            catch (ProjectImageRenderException error) when (tolerateImageFailure)
            {
                previewImageFailures[error.AssetId] = error;
                result.Add(new(document.Assets.Single(asset => asset.Id == error.AssetId), -1));
            }
        }
        return result.MoveToImmutable();
    }

    private ImageFrameIdentity ImageFrame(ProjectDocument document, ProjectLayer layer, MediaTime time)
    {
        var image = layer.Image!;
        var asset = document.Assets.Single(value => value.Id == image.AssetId);
        try
        {
            var frameIndex = image.Playback is { } playback ? images.GetMetadata(asset).GetFrameIndex(
                ImagePlaybackTiming.GetSourceTime(layer, time), playback.Mode) : 0;
            return new(asset, frameIndex);
        }
        catch (Exception error) when (IsImageFailure(error))
        {
            throw new ProjectImageRenderException(asset, error);
        }
    }

    private void DrawImage(ProjectDocument document, SKCanvas canvas, ProjectLayer source, MediaTime time)
    {
        var layer = source.Image!;
        var identity = ImageFrame(document, source, time);
        using var lease = OpenImageFrame(identity);
        var image = lease.Image;
        var crop = layer.Crop;
        var sourceBounds = new SKRect((float)(crop.X * image.Width), (float)(crop.Y * image.Height),
            (float)((crop.X + crop.Width) * image.Width), (float)((crop.Y + crop.Height) * image.Height));
        var width = (float)layer.Width;
        var height = (float)layer.Height;
        var bounds = new SKRect(0, 0, width, height);
        canvas.ClipRect(bounds, SKClipOperation.Intersect, true);
        if (layer.Fit != ImageFit.STRETCH)
        {
            var scale = layer.Fit == ImageFit.FIT ? Math.Min(width / sourceBounds.Width, height / sourceBounds.Height) :
                Math.Max(width / sourceBounds.Width, height / sourceBounds.Height);
            var targetWidth = sourceBounds.Width * scale;
            var targetHeight = sourceBounds.Height * scale;
            bounds = new((width - targetWidth) / 2, (height - targetHeight) / 2, (width + targetWidth) / 2, (height + targetHeight) / 2);
        }

        canvas.DrawImage(image, sourceBounds, bounds, new SKSamplingOptions(SKFilterMode.Linear));
    }

    private ImageFrameLease OpenImageFrame(ImageFrameIdentity identity)
    {
        try
        {
            return images.GetFrame(identity);
        }
        catch (Exception error) when (IsImageFailure(error))
        {
            throw new ProjectImageRenderException(identity.Asset, error);
        }
    }

    private static bool IsImageFailure(Exception error) => error is IOException or InvalidDataException or
        UnauthorizedAccessException or NotSupportedException or ArgumentException;

    private ProjectLayerGeometry? FindGeometry(ProjectDocument document, ImmutableArray<EvaluatedLayer> layers,
        Guid id, SKMatrix parentToWorld)
    {
        foreach (var layer in layers)
        {
            var geometry = Geometry(document, layer, parentToWorld);
            if (layer.Source.Id == id)
            {
                return geometry;
            }

        }

        return null;
    }

    private ProjectLayerGeometry Geometry(ProjectDocument document, EvaluatedLayer layer, SKMatrix parentToWorld)
    {
        SKRect bounds;
        var pivot = SKPoint.Empty;
        var basePosition = SKPoint.Empty;
        var hasInk = true;
        IReadOnlyList<SubtitleGraphemeGeometry>? visibleGraphemes = null;
        switch (layer.Source.Kind)
        {
            case LayerKind.SUBTITLE:
                var layout = Layout(document, layer);
                var visible = VisibleLayout(layer, layout.Snapshot);
                bounds = visible.Bounds;
                visibleGraphemes = ReferenceEquals(visible, layout.Snapshot) ? null : visible.Graphemes;
                pivot = layout.Pivot;
                basePosition = layout.BasePosition;
                hasInk = layout.HasInk;
                break;
            case LayerKind.SHAPE:
                using (var path = ProjectShapeGeometry.CreatePath(layer.Source.Shape!))
                {
                    bounds = path.TightBounds;
                    if (bounds.Width == 0 || bounds.Height == 0)
                    {
                        bounds.Inflate((float)Math.Max(0.5, layer.StrokeWidth / 2), (float)Math.Max(0.5, layer.StrokeWidth / 2));
                    }
                }

                break;
            case LayerKind.IMAGE:
                var image = layer.Source.Image!;
                bounds = new(0, 0, (float)image.Width, (float)image.Height);
                break;
            default:
                throw new InvalidDataException("Unknown layer kind.");
        }

        if (layer.Source.Kind != LayerKind.SUBTITLE && layer.Source.Position is { } position)
        {
            var resolvedBase = LayerPositionGeometry.BasePosition(position, new(document.Width, document.Height));
            var resolvedPivot = LayerPositionGeometry.LocalPivot(position, new(), LayerPositionGeometry.GraphicSize(layer.Source));
            basePosition = new((float)resolvedBase.X, (float)resolvedBase.Y);
            pivot = new((float)resolvedPivot.X, (float)resolvedPivot.Y);
        }

        var transform = layer.Transform;
        var effectivePivot = new SKPoint(pivot.X + (float)transform.AnchorX, pivot.Y + (float)transform.AnchorY);
        var local = SKMatrix.CreateTranslation(basePosition.X + (float)transform.X, basePosition.Y + (float)transform.Y);
        local = SKMatrix.Concat(local, SKMatrix.CreateRotationDegrees((float)transform.Rotation));
        local = SKMatrix.Concat(local, SKMatrix.CreateScale((float)transform.ScaleX, (float)transform.ScaleY));
        local = SKMatrix.Concat(local, SKMatrix.CreateTranslation(-effectivePivot.X, -effectivePivot.Y));
        return new(bounds, effectivePivot, basePosition, SKMatrix.Concat(parentToWorld, local), parentToWorld, hasInk)
        {
            VisibleGraphemes = visibleGraphemes,
            Shape = layer.Source.Shape,
            ShapeStrokeWidth = layer.StrokeWidth,
            ShapeHasFill = layer.Fill.Alpha > 0
        };
    }

    private SKTypeface Typeface(ProjectDocument document, SubtitleStyle style)
    {
        if (style.FontAssetId is { } id)
        {
            using var stream = assets.Open(document.Assets.Single(value => value.Id == id));
            using var data = SKData.Create(stream);
            return SKTypeface.FromData(data) ?? throw new InvalidDataException("The project font could not be opened.");
        }
        return SKTypeface.FromFamilyName(style.FontFamily, FontStyle(style));
    }

    private static SKColorFilter CreatePreviewWhiteFilter(float scale)
    {
        using var effect = SKRuntimeEffect.CreateColorFilter("uniform float scale; half4 main(half4 c) { return half4(c.rgb * scale, c.a); }", out var error)
            ?? throw new InvalidOperationException($"The preview reference-white conversion could not be created: {error}");
        using var uniforms = new SKRuntimeEffectUniforms(effect);
        uniforms["scale"] = scale;
        return effect.ToColorFilter(uniforms) ?? throw new InvalidOperationException("The preview reference-white conversion instance could not be created.");
    }

    private static SKBlender CreateAdditiveBlend()
    {
        using var effect = SKRuntimeEffect.CreateBlender("half4 main(half4 s, half4 d) { return half4(clamp(s.rgb + d.rgb, -65504.0, 65504.0), min(s.a + d.a, 1.0)); }", out var error)
            ?? throw new InvalidOperationException($"The linear HDR additive blend could not be created: {error}");
        return effect.ToBlender() ?? throw new InvalidOperationException("The linear HDR blend instance could not be created.");
    }

    private SKPaint Paint(SceneColor color)
    {
        var paint = new SKPaint { IsAntialias = true };
        try
        {
            paint.SetColor(new((float)color.Red, (float)color.Green, (float)color.Blue, (float)color.Alpha), linear);
            if (graphicsContext is not null && (color.Red is < 0 or > 1 || color.Green is < 0 or > 1 || color.Blue is < 0 or > 1))
            {
                extendedColorShader ??= new();
                extendedColorShader.Apply(paint, (float)color.Red, (float)color.Green, (float)color.Blue, (float)color.Alpha);
            }
            return paint;
        }
        catch
        {
            paint.Dispose();
            throw;
        }
    }

    private static SKPath Path(PathGeometry geometry)
    {
        var path = new SKPath();
        path.MoveTo((float)geometry.Start.X, (float)geometry.Start.Y);
        foreach (var segment in geometry.Segments)
        {
            path.CubicTo((float)segment.Control1.X, (float)segment.Control1.Y, (float)segment.Control2.X,
                (float)segment.Control2.Y, (float)segment.End.X, (float)segment.End.Y);
        }

        if (geometry.Closed)
        {
            path.Close();
        }

        return path;
    }

    private void ClearLayouts()
    {
        layouts.Clear();
        typographyProjections.Clear();
        scalingProjections.Clear();
        foreach (var shaper in actualTextShapers.Values)
        {
            shaper.Dispose();
        }
        textShapers.Clear();
        actualTextShapers.Clear();
        resolvedTextShapers.Clear();
    }
}
