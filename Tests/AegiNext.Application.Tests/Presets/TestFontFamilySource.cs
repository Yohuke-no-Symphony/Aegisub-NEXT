using System.Collections.Immutable;
using AegiNext.Core.Fonts;
using AegiNext.Core.Presets;

namespace AegiNext.Application.Tests.Presets;

internal sealed class TestFontFamilySource(IEnumerable<FontFamilyBundle>? families = null,
    IReadOnlyDictionary<string, string>? aliases = null) : IFontFamilySource
{
    private readonly ImmutableArray<FontFamilyBundle> entries = families?.ToImmutableArray() ?? [];
    internal int CaptureCount { get; private set; }
    internal EmbeddedSubtitleFont? LastPrimaryFont { get; private set; }

    public Task<FontFamilyBundle> CaptureAsync(string familyName, EmbeddedSubtitleFont? primaryFont = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CaptureCount++;
        LastPrimaryFont = primaryFont;
        familyName = aliases?.GetValueOrDefault(familyName) ?? familyName;
        return Task.FromResult(entries.FirstOrDefault(family => string.Equals(family.FamilyName, familyName, StringComparison.OrdinalIgnoreCase)) ??
            TestStylePackageStore.Family(familyName));
    }
}
