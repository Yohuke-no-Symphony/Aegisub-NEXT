using AegiNext.Desktop.EffectLibrary;
using AegiNext.Core.Presets;
using AegiNext.Desktop.Settings;

namespace AegiNext.Desktop.Tests;

public sealed class LibraryStyleDraftTests
{
    [Fact]
    public void ChangingTypographyPreservesEmbeddedFontAndUneditedHdrColors()
    {
        var original = CreatePreset();
        var draft = new LibraryStyleDraft(original);

        draft.UpdateStyle(draft.Preset.Style with { FontSize = 72, Bold = true, Margins = new(88, 88, 88) });

        Assert.Same(original.Font, draft.Preset.Font);
        Assert.Equal(original.Style.Fill, draft.Preset.Style.Fill);
        Assert.Equal(original.Style.ShadowColor, draft.Preset.Style.ShadowColor);
        Assert.Equal(72, draft.Preset.Style.FontSize);
        Assert.True(draft.Preset.Style.Bold);
        Assert.Equal(88, draft.Preset.Style.Margins.Vertical);
        Assert.Equal(64, original.Style.FontSize);
        Assert.False(original.Style.Bold);
    }

    [Fact]
    public void ExplicitFontFamilyChangeRemovesEmbeddedFontAndProjectAssetIdentity()
    {
        var draft = new LibraryStyleDraft(CreatePreset());

        draft.UpdateStyle(draft.Preset.Style with { FontFamily = "serif", FontAssetId = Guid.NewGuid() });

        Assert.Equal("serif", draft.Preset.Style.FontFamily);
        Assert.Null(draft.Preset.Font);
        Assert.Null(draft.Preset.Style.FontAssetId);
    }

    [Fact]
    public void DuplicateHasIndependentIdentityAndKeepsPortableFontAndOriginalStyle()
    {
        var original = CreatePreset();
        var draft = new LibraryStyleDraft(original);
        var copy = draft.Duplicate("Copy");

        Assert.NotEqual(original.Id, copy.Preset.Id);
        Assert.Equal("Copy", copy.Preset.Name);
        Assert.Same(original.Font, copy.Preset.Font);
        Assert.Same(original.Style, copy.Preset.Style);

        copy.Rename("Renamed");
        copy.UpdateStyle(copy.Preset.Style with { Italic = true });
        Assert.Equal("Original", draft.Preset.Name);
        Assert.False(draft.Preset.Style.Italic);
        Assert.True(copy.Preset.Style.Italic);
    }

    [Fact]
    public void RenameNormalizesUnicodeAndTrimsWithoutReplacingIdentity()
    {
        var original = CreatePreset();
        var draft = new LibraryStyleDraft(original);

        draft.Rename("  Cafe\u0301  ");

        Assert.Equal("Café", draft.Preset.Name);
        Assert.Equal(original.Id, draft.Preset.Id);
        Assert.Equal("Original", original.Name);
    }

    [Fact]
    public void NewNameSkipsCaseInsensitiveExistingNamesWithoutModifyingTheCollection()
    {
        var first = CreatePreset() with { Name = "new style" };
        var second = CreatePreset() with { Name = "New style 2" };
        var values = new[] { first, second };

        Assert.Equal("New style 3", LibraryStyleDraft.UniqueName("New style", values));
        Assert.Equal("Unused", LibraryStyleDraft.UniqueName("Unused", values));
        Assert.Equal(first, values[0]);
        Assert.Equal(second, values[1]);
    }

    private static SubtitleStylePreset CreatePreset()
    {
        return new(Guid.NewGuid(), "Original",
            new() { FontFamily = "Portable Family", Fill = new(2.5, 1.25, 0.5, 0.9), ShadowColor = new(1.5, 0.1, 0.2, 0.5) },
            new("portable.ttf", new string('a', 64), [1, 2, 3]));
    }

    [Fact]
    public void DuplicatingMaximumLengthNamesKeepsTheLimitAndCompleteUnicodeCharacters()
    {
        var original = new string('a', 127) + "🌙";
        var first = LibraryStyleDraft.UniqueName(original, []);
        Assert.Equal(new string('a', 127), first);
        var existing = CreatePreset() with { Name = first };
        var next = LibraryStyleDraft.UniqueName(original, [existing]);
        Assert.Equal(new string('a', 126) + " 2", next);
        Assert.Equal(128, next.Length);
        Assert.True(next.IsNormalized());
    }
}
