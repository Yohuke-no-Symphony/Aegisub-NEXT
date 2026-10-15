using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using SkiaSharp;

namespace AegiNext.Rendering.Projects;

public sealed partial class ProjectSceneRenderer
{
    /// <summary>在指定字幕局部矩形内绘制透明编辑预览；保持工程排版，可固定普通、未激活或高亮外观，不应用图层动画与变换，调用方释放表面。</summary>
    public LinearRenderSurface RenderSubtitlePreview(ProjectDocument document, SubtitleLine subtitle,
        MediaTime localTime, SKRect cropBounds, SubtitlePreviewMode previewMode = SubtitlePreviewMode.TIMED)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(subtitle);
        if (!Enum.IsDefined(previewMode))
        {
            throw new ArgumentOutOfRangeException(nameof(previewMode));
        }
        if (!float.IsFinite(cropBounds.Left) || !float.IsFinite(cropBounds.Top) ||
            !float.IsFinite(cropBounds.Right) || !float.IsFinite(cropBounds.Bottom) ||
            cropBounds.Width is <= 0 or > 32768 || cropBounds.Height is <= 0 or > 32768 ||
            Math.Ceiling(cropBounds.Width) * Math.Ceiling(cropBounds.Height) > 33177600)
        {
            throw new ArgumentOutOfRangeException(nameof(cropBounds));
        }
        Prepare(document);
        var surface = new LinearRenderSurface(new((int)Math.Ceiling(cropBounds.Width),
            (int)Math.Ceiling(cropBounds.Height), (float)document.ReferenceWhiteNits), graphicsContext);
        try
        {
            surface.Canvas.Translate(-cropBounds.Left, -cropBounds.Top);
            var source = new ProjectLayer { Kind = LayerKind.SUBTITLE, SubtitleId = subtitle.Id };
            var layer = new EvaluatedLayer(source, localTime, new(), 1, subtitle.Style.Fill, subtitle.Style.Stroke,
                subtitle.Style.StrokeWidth, 0, subtitle)
            {
                LetterSpacing = subtitle.Style.LetterSpacing, FillBlur = subtitle.Style.FillBlur,
                StrokeBlur = subtitle.Style.StrokeBlur
            };
            DrawSubtitle(document, surface.Canvas, layer, previewMode);
            surface.Canvas.ResetMatrix();
            return surface;
        }
        catch
        {
            surface.Dispose();
            throw;
        }
    }

    private void DrawSubtitle(ProjectDocument document, SKCanvas canvas, EvaluatedLayer layer,
        SubtitlePreviewMode previewMode = SubtitlePreviewMode.TIMED, ProjectiveMapping? outerProjection = null)
    {
        layer = CanvasLayer(document, layer);
        var subtitle = layer.Subtitle!;
        var layout = Layout(document, layer);
        var matrices = RangeMatrices(layer, layout.Snapshot);
        outerProjection ??= ProjectiveMapping.Affine(canvas.TotalMatrix);
        foreach (var line in layout.Lines)
        {
            foreach (var run in line.Runs)
            {
                var normal = SubtitleAnimationEvaluation.ApplyStyleAnimations(layer, run.Style, run.Utf16Offset, SubtitleAnimationState.NORMAL, includeTypography: false);
                if (layer.AnimationRanges.IsEmpty && layer.AnimationValues.Keys.All(target => target.TextRangeId is null && target.State == SubtitleAnimationState.NORMAL) &&
                    layer.EffectStyleOperations.All(operation => operation.Target.TextRangeId is null && operation.Target.State == SubtitleAnimationState.NORMAL) &&
                    (previewMode == SubtitlePreviewMode.NORMAL || run.Karaoke.IsEmpty && previewMode == SubtitlePreviewMode.TIMED))
                {
                    DrawSubtitleRun(canvas, run, normal);
                    continue;
                }
                DrawKaraokeRun(canvas, run, subtitle, layer, normal, previewMode, matrices, outerProjection);

            }
        }
    }

    private void DrawSubtitleRun(SKCanvas canvas, SubtitleLayoutRun run, SubtitleStyle style)
    {
        if (style.ShadowColor.Alpha > 0)
        {
            using var shadow = Paint(style.ShadowColor);
            using var filter = style.ShadowBlur > 0 ? SKMaskFilter.CreateBlur(SKBlurStyle.Normal, (float)style.ShadowBlur) : null;
            shadow.MaskFilter = filter;
            DrawRunInk(canvas, run, shadow, new((float)style.ShadowOffset.X, (float)style.ShadowOffset.Y));
        }
        if (style.StrokeWidth > 0)
        {
            using var stroke = Paint(style.Stroke);
            stroke.Style = SKPaintStyle.Stroke;
            stroke.StrokeWidth = (float)style.StrokeWidth * 2;
            stroke.StrokeJoin = SKStrokeJoin.Round;
            using var filter = style.StrokeBlur > 0 ? SKMaskFilter.CreateBlur(SKBlurStyle.Normal, (float)style.StrokeBlur) : null;
            stroke.MaskFilter = filter;
            DrawRunInk(canvas, run, stroke, SKPoint.Empty);
        }
        using var fill = Paint(style.Fill);
        using var fillFilter = style.FillBlur > 0 ? SKMaskFilter.CreateBlur(SKBlurStyle.Normal, (float)style.FillBlur) : null;
        fill.MaskFilter = fillFilter;
        DrawRunInk(canvas, run, fill, SKPoint.Empty);
    }

    private static void DrawRunInk(SKCanvas canvas, SubtitleLayoutRun run, SKPaint paint, SKPoint offset)
    {
        var position = new SKPoint(run.Position.X + offset.X, run.Position.Y + offset.Y);
        canvas.DrawText(run.Shape.GetBlob(), position.X, position.Y, paint);
        foreach (var decoration in Decorations(run))
        {
            var bounds = decoration;
            bounds.Offset(position);
            canvas.DrawRect(bounds, paint);
        }
    }
}
