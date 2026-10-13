using System.Security.Cryptography;
using System.Text;
using AegiNext.Rendering.Fonts;

namespace AegiNext.Desktop.Rendering;

internal sealed record EffectPresetPreviewRequest
{
    /// <summary>冻结卡片身份、脚本版本、宿主 DPI 和不可变字体目录；后续 UI 更新须创建新请求。</summary>
    public EffectPresetPreviewRequest(string presetId, string source, long revision, double renderScaling,
        SystemFontCatalog? fontCatalog)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(presetId);
        ArgumentNullException.ThrowIfNull(source);
        if (!double.IsFinite(renderScaling) || renderScaling <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(renderScaling));
        }
        PresetId = presetId;
        Source = source;
        Revision = revision;
        RenderScaling = renderScaling;
        FontCatalog = fontCatalog;
        SourceHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
    }

    public string PresetId { get; }
    public string Source { get; }
    public long Revision { get; }
    public double RenderScaling { get; }
    public SystemFontCatalog? FontCatalog { get; }
    public string SourceHash { get; }
}
