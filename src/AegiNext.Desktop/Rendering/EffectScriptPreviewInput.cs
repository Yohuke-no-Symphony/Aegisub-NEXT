using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using AegiNext.Core.Effects;
using System.Collections.Immutable;

namespace AegiNext.Desktop.Rendering;

internal sealed record EffectScriptPreviewInput(string Source, EffectPresetPreviewOptions Options,
    SubtitleStylePreset? StylePreset = null)
{
    private ImmutableDictionary<string, EffectScriptParameterValue> parameters = EffectScriptParameterIdentity.Normalize(Source);
    public int OutputWidth { get; init; } = 640;
    public int OutputHeight { get; init; } = 360;
    public ImmutableDictionary<string, EffectScriptParameterValue> Parameters
    {
        get => parameters;
        init => parameters = EffectScriptParameterIdentity.Normalize(Source, value);
    }

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
        return Source == other.Source && Parameters.Count == other.Parameters.Count &&
            Parameters.All(pair => other.Parameters.TryGetValue(pair.Key, out var value) && value == pair.Value) && Options.Text == other.Options.Text &&
            Options.Duration == other.Options.Duration && Options.CanvasWidth == other.Options.CanvasWidth &&
            Options.CanvasHeight == other.Options.CanvasHeight && Options.FramesPerSecond == other.Options.FramesPerSecond &&
            GetStyle() == other.GetStyle();
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

}
