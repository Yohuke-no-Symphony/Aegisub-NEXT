using AegiNext.Core.Presets;

namespace AegiNext.Core.Fonts;

/// <summary>从当前可用字体来源捕获包含全部可用字体文件的可迁移字体族。</summary>
public interface IFontFamilySource
{
    /// <summary>捕获指定字体族；提供主字体时以其真实族标识确定目标并合并其余可用文件。</summary>
    Task<FontFamilyBundle> CaptureAsync(string familyName, EmbeddedSubtitleFont? primaryFont = null,
        CancellationToken cancellationToken = default);
}
