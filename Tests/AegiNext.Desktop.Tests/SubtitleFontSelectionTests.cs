using System.Security.Cryptography;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.EffectLibrary.Styles;
using AegiNext.Rendering.Fonts;

namespace AegiNext.Desktop.Tests;

public sealed class SubtitleFontSelectionTests
{
    [Fact]
    public void BoldToggleUsesRegularThenBoldAndPreservesItalicFormatting()
    {
        var fonts = Fonts();
        var black = new SubtitleStyle { FontFamily = "Fixture", FontVariant = Variant("Black", 900), Bold = true, Italic = true };
        var regular = fonts.ToggleBold(black, false).ApplyTo(black);
        Assert.Equal(400, regular.FontVariant?.Weight);
        Assert.False(regular.Bold);
        Assert.True(regular.Italic);
        var bold = fonts.ToggleBold(regular, true).ApplyTo(regular);
        Assert.Equal(700, bold.FontVariant?.Weight);
        Assert.True(bold.Bold);
        Assert.True(bold.Italic);
    }

    [Fact]
    public void MissingStylesUseLegacyFormattingAndEmbeddedFontKeepsItsBinding()
    {
        var fonts = new SubtitleFontSelectionService(Array.Empty<SystemFontFace>());
        var style = new SubtitleStyle { FontVariant = Variant("Black", 900), Bold = true };
        var changed = fonts.ToggleBold(style, false).ApplyTo(style);
        Assert.Null(changed.FontVariant);
        Assert.False(changed.Bold);
        var imported = new SubtitleStyle { FontAssetId = Guid.NewGuid() };
        Assert.Equal(imported.FontAssetId, Fonts().ToggleBold(imported, true).ApplyTo(imported).FontAssetId);
    }

    [Fact]
    public void SettingsSynchronizeBlackWithoutChangingItAndClearSameFamilyEmbeddedPayload()
    {
        var font = new EmbeddedSubtitleFont("fixture.ttf", Convert.ToHexStringLower(SHA256.HashData([1, 2, 3])), [1, 2, 3]);
        var model = new StyleLibraryViewModel();
        model.SetFonts(Fonts());
        model.UpdateStyles([new(Guid.NewGuid(), "Embedded", new() { FontFamily = "Fixture" }, font)]);
        model.CommitFont(new FontSelection("Fixture", Variant("Black", 900), true));
        Assert.Null(model.Draft!.Font);
        Assert.Equal(900, model.Draft.Style.FontVariant?.Weight);
        Assert.True(model.Bold);
        model.Bold = true;
        Assert.Equal(900, model.Draft.Style.FontVariant?.Weight);
        model.Bold = false;
        Assert.Equal(400, model.Draft.Style.FontVariant?.Weight);
        model.Bold = true;
        Assert.Equal(700, model.Draft.Style.FontVariant?.Weight);
        model.CommitFont(new FontSelection("Fixture", Variant("SemiBold", 600), true));
        Assert.Equal(600, model.Draft.Style.FontVariant?.Weight);
        Assert.False(model.Bold);
    }

    [Fact]
    public void SelectionDraftResolvesDisplayNameAndRestoresOnlyTheFontField()
    {
        var style = new SubtitleStyle { FontFamily = "Fixture", FontVariant = Variant("SemiBold", 600) };
        var draft = new SubtitleDetailsStyleDraft(Fonts());
        draft.Load(style);
        Assert.Equal("Fixture SemiBold", draft.FontFamily);
        Assert.False(draft.IsDirty);
        draft.FontFamily = "Fixture Heavy";
        draft.FontSizeText = "31";
        var changed = draft.Read(style).ApplyTo(style);
        Assert.Equal("Fixture", changed.FontFamily);
        Assert.Equal(900, changed.FontVariant?.Weight);
        Assert.True(changed.Bold);
        Assert.Equal(31, changed.FontSize);
        draft.RestoreField(nameof(draft.FontFamily));
        Assert.Equal("Fixture SemiBold", draft.FontFamily);
        Assert.Equal("31", draft.FontSizeText);
        Assert.Equal(style.FontVariant, draft.Read(style).ApplyTo(style).FontVariant);
    }

    [Fact]
    public void UnavailableNamedIdentitySurvivesUnrelatedDraftEdits()
    {
        var style = new SubtitleStyle { FontFamily = "Missing", FontVariant = Variant("Designer Black", 913), Bold = true };
        var draft = new SubtitleDetailsStyleDraft(Fonts());
        draft.Load(style);
        draft.FontSizeText = "31";
        Assert.Equal(style.FontVariant, draft.Read(style).ApplyTo(style).FontVariant);
        Assert.Equal(SubtitleFontSelectionService.FromStyle(style), Fonts().Resolve("Missing Designer Black", style));
    }

    private static SubtitleFontSelectionService Fonts() => new(new[]
    {
        Face("Regular", 400), Face("SemiBold", 600), Face("Bold", 700), Face("Black", 900)
    });

    private static SystemFontFace Face(string name, int weight) => new()
    {
        FamilyName = "Fixture", Aliases = ["Fixture", "Localized Fixture"], Variant = Variant(name, weight)
    };

    private static SubtitleFontVariant Variant(string name, int weight) => new()
    {
        Name = name, PostScriptName = "Fixture-" + name.Replace(" ", string.Empty), Weight = weight
    };
}
