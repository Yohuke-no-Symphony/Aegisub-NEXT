using AegiNext.Application;
using AegiNext.Core.Effects;
using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Rendering;

internal sealed record EffectPresetPreviewScene(ProjectDocument Document)
{
    internal static EffectPresetPreviewScene Create(EffectPresetPreviewOptions options, string source,
        CancellationToken cancellationToken)
    {
        options.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        var script = EffectScriptParser.Parse(source);
        var pathFrame = script.Segments.Concat(script.Scopes.SelectMany(scope => scope.Segments))
            .SelectMany(segment => segment.Keyframes).FirstOrDefault(frame => frame.Property == EffectScriptProperty.PATH_PROGRESS);
        if (pathFrame is not null)
        {
            throw new EffectPresetPreviewRequirementException(EffectPresetPreviewRequirement.MOTION_PATH, pathFrame.Line, pathFrame.Column);
        }
        var subtitle = new SubtitleLine
        {
            Text = options.Text,
            End = options.Duration,
            Style = new()
            {
                FontFamily = options.FontFamily,
                FontSize = options.FontSize,
                Alignment = TextAlignment.MIDDLE_CENTER,
                Margins = new(options.Margin, options.Margin, options.Margin),
                Scaling = new()
                {
                    ReferenceWidth = options.CanvasWidth,
                    ReferenceHeight = options.CanvasHeight
                }
            }
        };
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SUBTITLE,
            SubtitleId = subtitle.Id,
            End = options.Duration
        };
        var document = new ProjectDocument
        {
            Width = options.CanvasWidth,
            Height = options.CanvasHeight,
            FrameRate = new(options.FramesPerSecond, 1),
            Subtitles = [subtitle],
            Layers = [layer]
        };
        var prepared = ProjectEditingOperations.ApplyEffectScript(document, [layer.Id],
            script, cancellationToken);
        return new(prepared);
    }
}
