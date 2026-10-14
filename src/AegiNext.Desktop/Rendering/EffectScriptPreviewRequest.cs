using AegiNext.Rendering.Fonts;

namespace AegiNext.Desktop.Rendering;

internal sealed record EffectScriptPreviewRequest
{
    /// <summary>冻结样例、显示比例和借用字体目录，并在排程前验证像素预算。</summary>
    public EffectScriptPreviewRequest(Guid draftId, long revision, EffectScriptPreviewInput input,
        double renderScaling = 1, SystemFontCatalog? fontCatalog = null)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(draftId, Guid.Empty);
        ArgumentNullException.ThrowIfNull(input);
        input.Validate();
        if (!double.IsFinite(renderScaling) || renderScaling <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(renderScaling));
        }
        DraftId = draftId;
        Revision = revision;
        Input = input;
        RenderScaling = renderScaling;
        FontCatalog = fontCatalog;
        _ = GetOutputSize();
    }

    public Guid DraftId { get; }
    public long Revision { get; }
    public EffectScriptPreviewInput Input { get; }
    public double RenderScaling { get; }
    public SystemFontCatalog? FontCatalog { get; }

    internal (int Width, int Height) GetOutputSize()
    {
        return EffectPresetPreviewCacheKey.GetOutputSize(Input.Options with
        {
            CanvasWidth = Input.OutputWidth, CanvasHeight = Input.OutputHeight
        }, RenderScaling);
    }
}
