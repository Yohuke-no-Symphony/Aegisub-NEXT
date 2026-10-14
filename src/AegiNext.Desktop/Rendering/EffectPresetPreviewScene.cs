using AegiNext.Application;
using AegiNext.Core.Effects;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using AegiNext.Rendering.Projects;

namespace AegiNext.Desktop.Rendering;

internal sealed record EffectPresetPreviewScene(ProjectDocument Document, IProjectAssetResolver Assets)
{
    internal static EffectPresetPreviewScene Create(EffectPresetPreviewOptions options, string source,
        CancellationToken cancellationToken)
    {
        return Create(options, source, null, cancellationToken);
    }

    internal static EffectPresetPreviewScene Create(EffectPresetPreviewOptions options, string source,
        SubtitleStylePreset? stylePreset, CancellationToken cancellationToken)
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
        var seed = stylePreset is null ? CreateDefaultSeed(options) :
            SubtitleStylePreviewScene.Create(stylePreset, options.CanvasWidth, options.CanvasHeight, options.Text);
        var subtitle = seed.Document.Subtitles[0] with { End = options.Duration };
        var layer = seed.Document.Layers[0] with { End = options.Duration };
        var document = seed.Document with
        {
            FrameRate = new(options.FramesPerSecond, 1),
            Subtitles = [subtitle],
            Layers = [layer]
        };
        var prepared = ProjectEditingOperations.ApplyEffectScript(document, [layer.Id],
            script, cancellationToken);
        return new(prepared, seed.Assets);
    }

    private static SubtitleStylePreviewScene CreateDefaultSeed(EffectPresetPreviewOptions options)
    {
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
                Scaling = new() { ReferenceWidth = options.CanvasWidth, ReferenceHeight = options.CanvasHeight }
            }
        };
        var document = new ProjectDocument
        {
            Width = options.CanvasWidth, Height = options.CanvasHeight, Subtitles = [subtitle],
            Layers = [new() { Kind = LayerKind.SUBTITLE, SubtitleId = subtitle.Id, End = options.Duration }]
        };
        return new(document, new DirectoryProjectAssetResolver(Path.GetTempPath()));
    }
}
