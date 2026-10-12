using AegiNext.Core.Projects;
using AegiNext.Rendering.Fonts;
using AegiNext.Rendering.SubtitleFormats;
using AegiNext.SubtitleFormats;

namespace AegiNext.Rendering.Tests;

public sealed class AssFontWeightResolverTests
{
    [Theory]
    [InlineData("Fixture")]
    [InlineData("fixture")]
    [InlineData("Alias")]
    [InlineData("ALIAS")]
    public void MatchesCanonicalFamilyOrAliasWithExactDesignFeatures(string family)
    {
        var face = Face("Fixture", "SemiBold Italic", 600, italic: true) with { Aliases = ["Alias"] };
        var resolver = new AssFontWeightResolver([face]);

        Assert.Equal(face.Variant, resolver.ResolveVariant(family, 600, 5, true));
        Assert.Null(resolver.ResolveVariant(family, 599, 5, true));
        Assert.Null(resolver.ResolveVariant(family, 600, 4, true));
        Assert.Null(resolver.ResolveVariant(family, 600, 5, false));
        Assert.Null(resolver.ResolveVariant("Fixture SemiBold Italic", 600, 5, true));
        Assert.Null(resolver.ResolveVariant("Other", 600, 5, true));
    }

    [Fact]
    public void CanonicalFamilyPrecedesStaticAliasAndStaticFacePrecedesVariableWithinAFamily()
    {
        var alias = Face("Other", "Alias Face", 600) with { Aliases = ["Fixture"] };
        var variable = Face("Fixture", "Variable Face", 600) with { IsVariable = true };
        Assert.Equal(variable.Variant, new AssFontWeightResolver([alias, variable]).ResolveVariant("Fixture", 600, 5, false));

        var staticFace = Face("Fixture", "Static Face", 600);
        Assert.Equal(staticFace.Variant,
            new AssFontWeightResolver([variable, alias, staticFace]).ResolveVariant("Fixture", 600, 5, false));
    }

    [Fact]
    public void NameThenPostScriptOrdinalOrderIsIndependentOfCatalogEnumeration()
    {
        var laterName = Face("Fixture", "b", 600);
        var laterPostScript = Face("Fixture", "A", 600) with { Variant = Variant("A", 600) with { PostScriptName = "b" } };
        var selected = laterPostScript with { Variant = laterPostScript.Variant with { PostScriptName = "A" } };
        SystemFontFace[] faces = [laterName, laterPostScript, selected];

        Assert.Equal(selected.Variant, new AssFontWeightResolver(faces).ResolveVariant("Fixture", 600, 5, false));
        Assert.Equal(selected.Variant, new AssFontWeightResolver(faces.Reverse()).ResolveVariant("Fixture", 600, 5, false));
    }

    [Fact]
    public void CapturedFacesAreStableAfterTheInputCollectionChanges()
    {
        var face = Face("Fixture", "SemiBold", 600);
        var faces = new List<SystemFontFace> { face };
        var resolver = new AssFontWeightResolver(faces);
        faces.Clear();

        Assert.Equal(face.Variant, resolver.ResolveVariant("Fixture", 600, 5, false));
        Assert.Null(new AssFontWeightResolver([]).ResolveVariant("Fixture", 600, 5, false));
    }

    [Fact]
    public void AssFileParserStoresTheRealResolvedVariantWithoutAWeightLoss()
    {
        var face = Face("Fixture", "SemiBold Italic", 600, italic: true) with { Aliases = ["Alias"] };
        var resolver = new AssFontWeightResolver([face]);
        var imported = AssSubtitleFormat.ParseAtSourceResolution("[Script Info]\nPlayResX: 1920\nPlayResY: 1080\n[Events]\n" +
            "Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n" +
            "Dialogue: 0,0:00:00.00,0:00:02.00,Default,,0,0,0,,{\\fnAlias\\b600\\i1}x\n",
            fontWeightResolver: resolver);
        var parsed = imported.Result;
        var line = Assert.Single(parsed.Lines);
        var style = line.InlineSpans.FirstOrDefault()?.Style.ApplyTo(line.Style) ?? line.Style;

        Assert.Equal(face.Variant, style.FontVariant);
        Assert.False(style.Bold);
        Assert.True(style.Italic);
        Assert.DoesNotContain(parsed.Diagnostics, diagnostic => diagnostic.Code == "Ass.FontWeight");
    }

    private static SystemFontFace Face(string family, string name, int weight, bool italic = false) => new()
    {
        FamilyName = family, Variant = Variant(name, weight, italic)
    };

    private static SubtitleFontVariant Variant(string name, int weight, bool italic = false) => new()
    {
        Name = name, PostScriptName = name, Weight = weight, Width = 5, Italic = italic
    };
}
