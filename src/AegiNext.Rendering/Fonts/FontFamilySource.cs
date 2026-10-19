using System.Collections.Immutable;
using AegiNext.Core.Fonts;
using AegiNext.Core.Presets;

namespace AegiNext.Rendering.Fonts;

/// <summary>从当前应用字体快照捕获原始字体族；未被应用族覆盖时读取系统安装来源。</summary>
public sealed class FontFamilySource : IFontFamilySource
{
    private readonly Func<SystemFontCatalog?> catalog;

    /// <summary>借用应用持有的当前字体快照，不缓存随字体库更新而失效的目录。</summary>
    public FontFamilySource(Func<SystemFontCatalog?> catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        this.catalog = catalog;
    }

    /// <summary>使用固定的不可变字体目录作为本次捕获来源。</summary>
    public FontFamilySource(SystemFontCatalog catalog) : this(() => catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
    }

    /// <inheritdoc />
    public Task<FontFamilyBundle> CaptureAsync(string familyName, EmbeddedSubtitleFont? primaryFont = null,
        CancellationToken cancellationToken = default)
    {
        var current = catalog();
        return Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = current ?? new SystemFontCatalog();
            var target = FontFamilyCapture.ResolvePrimaryFamily(familyName, primaryFont);
            var matching = snapshot.Faces.Where(face => face.IsApplicationFont && FontFamilyCapture.Matches(face, target)).ToArray();
            if (matching.Length == 0)
            {
                return await new InstalledFontFamilySource(snapshot).CaptureAsync(target, primaryFont, cancellationToken).ConfigureAwait(false);
            }
            var canonical = matching[0].FamilyName;
            var files = matching.Select(face => face.ApplicationSource!.File)
                .Concat(snapshot.ApplicationFonts.Where(family => string.Equals(family.FamilyName, canonical, StringComparison.OrdinalIgnoreCase) ||
                    matching.Any(face => FontFamilyCapture.Matches(face, family.FamilyName))).SelectMany(family => family.Files));
            return FontFamilyCapture.Create(canonical, files, primaryFont);
        }, cancellationToken);
    }
}
