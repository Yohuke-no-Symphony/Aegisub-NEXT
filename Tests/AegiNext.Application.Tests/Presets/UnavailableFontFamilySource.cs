using AegiNext.Core.Fonts;
using AegiNext.Core.Presets;

namespace AegiNext.Application.Tests.Presets;

internal sealed class UnavailableFontFamilySource : IFontFamilySource
{
    internal int CaptureCount { get; private set; }

    public Task<FontFamilyBundle> CaptureAsync(string familyName, EmbeddedSubtitleFont? primaryFont = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CaptureCount++;
        throw new InvalidDataException($"找不到字体族 {familyName} 的原始字体文件。");
    }
}
