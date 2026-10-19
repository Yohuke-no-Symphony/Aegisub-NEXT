using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using AegiNext.Rendering.Projects;

namespace AegiNext.Desktop.Rendering;

internal sealed record SubtitleStylePreviewScene(ProjectDocument Document, IProjectAssetResolver Assets)
{
    internal static SubtitleStylePreviewScene Create(SubtitleStylePreset preset, int width, int height, string text)
    {
        SubtitleStylePresetValidator.Validate(preset with { Name = "Preview", TimingPostProcessor = null });
        var subtitle = new SubtitleLine
        {
            Text = text, Style = preset.Style
        };
        var document = new ProjectDocument
        {
            Width = width, Height = height, Subtitles = [subtitle],
            Layers = [new() { Kind = LayerKind.SUBTITLE, SubtitleId = subtitle.Id, End = subtitle.End }]
        };
        return new(document, new DirectoryProjectAssetResolver(Path.GetTempPath()));
    }
}
