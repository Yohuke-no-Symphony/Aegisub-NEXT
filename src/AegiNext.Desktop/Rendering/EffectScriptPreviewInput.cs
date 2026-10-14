using AegiNext.Core.Presets;
using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Rendering;

internal sealed record EffectScriptPreviewInput(string Source, EffectPresetPreviewOptions Options,
    SubtitleStylePreset? StylePreset = null)
{
    public int OutputWidth { get; init; } = 640;
    public int OutputHeight { get; init; } = 360;

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Source);
        ArgumentNullException.ThrowIfNull(Options);
        Options.Validate();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(OutputWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(OutputHeight);
    }

    internal bool HasSameScene(EffectScriptPreviewInput other)
    {
        return Source == other.Source && Options.Text == other.Options.Text &&
            Options.Duration == other.Options.Duration && Options.CanvasWidth == other.Options.CanvasWidth &&
            Options.CanvasHeight == other.Options.CanvasHeight && Options.FramesPerSecond == other.Options.FramesPerSecond &&
            GetStyle() == other.GetStyle() && HasSameFont(StylePreset?.Font, other.StylePreset?.Font);
    }

    internal bool HasSameVisuals(EffectScriptPreviewInput other)
    {
        return HasSameScene(other) && Options.BackgroundBgra == other.Options.BackgroundBgra;
    }

    private SubtitleStyle GetStyle()
    {
        return StylePreset?.Style ?? new()
        {
            FontFamily = Options.FontFamily,
            FontSize = Options.FontSize,
            Alignment = TextAlignment.MIDDLE_CENTER,
            Margins = new(Options.Margin, Options.Margin, Options.Margin),
            Scaling = new() { ReferenceWidth = Options.CanvasWidth, ReferenceHeight = Options.CanvasHeight }
        };
    }

    private static bool HasSameFont(EmbeddedSubtitleFont? first, EmbeddedSubtitleFont? second)
    {
        return first is null ? second is null : second is not null && first.Sha256 == second.Sha256 &&
            (first.Data == second.Data || first.Data.AsSpan().SequenceEqual(second.Data.AsSpan()));
    }
}
