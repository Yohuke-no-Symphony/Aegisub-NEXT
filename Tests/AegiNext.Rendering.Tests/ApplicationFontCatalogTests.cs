using AegiNext.Core.Fonts;
using AegiNext.Core.Projects;
using AegiNext.Rendering.Fonts;
using AegiNext.Rendering.Projects;
using SkiaSharp;

namespace AegiNext.Rendering.Tests;

public sealed class ApplicationFontCatalogTests
{
    [Fact]
    public void ApplicationFamilyReplacesEverySystemVariantAndKeepsRawFiles()
    {
        var stale = new SystemFontFace
        {
            FamilyName = "Noto Sans", Variant = new() { Name = "Removed", Weight = 555 }
        };
        var system = new SystemFontCatalog([stale]);
        var bundle = ApplicationFontFixture.Latin();
        var catalog = new SystemFontCatalog([bundle], system);

        Assert.Equal(9, catalog.Faces.Length);
        Assert.All(catalog.Faces, face => Assert.True(face.IsApplicationFont));
        Assert.DoesNotContain(catalog.Faces, face => face.Variant.Name == "Removed");
        Assert.Equal(bundle, Assert.Single(catalog.ApplicationFonts));
    }

    [Theory]
    [InlineData(false, 400)]
    [InlineData(true, 700)]
    public void FamilyWithoutVariantSelectsRealPrivateFace(bool bold, int weight)
    {
        var catalog = new SystemFontCatalog([ApplicationFontFixture.Latin()], SystemFontCatalog.Empty);
        var resolved = new SystemFontResolver(catalog).Resolve(new() { FontFamily = "Noto Sans", Bold = bold });
        using var typeface = resolved.Typeface;

        Assert.True(resolved.Face!.IsApplicationFont);
        Assert.Equal(weight, resolved.Face.Variant.Weight);
        Assert.Equal(weight, typeface.FontWeight);
    }

    [Fact]
    public void PreparedNamedInstancesKeepGlyphsAndMetricsAndRenderDistinctNamePreviews()
    {
        var catalog = new SystemFontCatalog([ApplicationFontFixture.Latin()], SystemFontCatalog.Empty);
        var resolver = new SystemFontResolver(catalog);
        var regular = Assert.Single(catalog.Faces, face => face.Variant.Name == "Regular");
        var black = Assert.Single(catalog.Faces, face => face.Variant.Name == "Black");
        var first = resolver.Resolve(new() { FontFamily = regular.FamilyName, FontVariant = regular.Variant });
        var second = resolver.Resolve(new() { FontFamily = black.FamilyName, FontVariant = black.Variant, Bold = true });
        using var regularShaper = new TextShaper(first.Typeface, face: first.Face, explicitVariant: true);
        using var blackShaper = new TextShaper(second.Typeface, bold: true, face: second.Face, explicitVariant: true);
        using var regularRun = regularShaper.Shape("W", 1000, TextDirection.LEFT_TO_RIGHT, "en");
        using var blackRun = blackShaper.Shape("W", 1000, TextDirection.LEFT_TO_RIGHT, "en");

        Assert.True(first.IsExactMatch);
        Assert.True(second.IsExactMatch);
        Assert.Equal(4515, first.Typeface.GlyphCount);
        Assert.Equal(first.Typeface.GlyphCount, second.Typeface.GlyphCount);
        Assert.InRange(Math.Abs(regularRun.AdvanceWidth - 930), 0, 1);
        Assert.InRange(Math.Abs(blackRun.AdvanceWidth - 1039), 0, 1);
        Assert.False(blackShaper.SynthesizesBold);

        var previews = new SystemFontNamePreviewRenderer(() => catalog);
        var regularRequest = new FontNamePreviewRequest(regular.FamilyName, regular.Variant, "Noto Sans", 20, 1, 500);
        var blackRequest = regularRequest with { Variant = black.Variant };
        using var regularPreviewFace = Assert.IsAssignableFrom<IFontNamePreviewFace>(previews.Resolve(regularRequest, default));
        using var blackPreviewFace = Assert.IsAssignableFrom<IFontNamePreviewFace>(previews.Resolve(blackRequest, default));
        var regularPreview = Assert.IsType<FontNamePreview>(regularPreviewFace.Render(regularRequest, default));
        var blackPreview = Assert.IsType<FontNamePreview>(blackPreviewFace.Render(blackRequest, default));

        Assert.True(regular.IsVariable);
        Assert.True(black.IsVariable);
        Assert.Contains(regularPreview.Alpha.ToArray(), value => value > 0);
        Assert.Contains(blackPreview.Alpha.ToArray(), value => value > 0);
        Assert.NotEqual(regularPreviewFace.Fingerprint, blackPreviewFace.Fingerprint);
        Assert.False(regularPreview.Alpha.Span.SequenceEqual(blackPreview.Alpha.Span));
    }

    [Fact]
    public void CollectionNonFirstFaceIsAvailableWithoutNativeRegistration()
    {
        var font = ApplicationFontFixture.Collection();
        var catalog = new SystemFontCatalog([new FontFamilyBundle("Noto Sans Arabic", [font])], SystemFontCatalog.Empty);
        var face = catalog.Faces.First(value => value.Variant.Weight == 400 && value.Variant.Width == 5);
        var resolved = new SystemFontResolver(catalog).Resolve(new()
        {
            FontFamily = face.FamilyName, FontVariant = face.Variant
        });
        using var typeface = resolved.Typeface;
        using var shaper = new TextShaper(typeface, face: resolved.Face, explicitVariant: true);
        using var run = shaper.Shape("مرحبا", 64, TextDirection.RIGHT_TO_LEFT, "ar");

        Assert.Equal(1, face.CollectionIndex);
        Assert.True(resolved.IsExactMatch);
        Assert.True(run.AdvanceWidth > 0);
    }

    [Fact]
    public void MissingGraphemeCanUseAnotherPrivateFamily()
    {
        var catalog = new SystemFontCatalog([ApplicationFontFixture.Latin(), ApplicationFontFixture.Arabic()], SystemFontCatalog.Empty);
        var subtitle = new SubtitleLine { Text = "Aمرحبا", Style = new() { FontFamily = "Noto Sans" } };
        var document = new ProjectDocument
        {
            Subtitles = [subtitle], Layers = [new() { SubtitleId = subtitle.Id, End = subtitle.End }]
        };
        using var renderer = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(AppContext.BaseDirectory), catalog);

        var layout = renderer.MeasureSubtitleTextLayout(document, subtitle);

        Assert.Contains(layout.Runs, run => run.ResolvedFontFamily == "Noto Sans Arabic");
        Assert.Contains(layout.Runs, run => run.ResolvedFontFamily == "Noto Sans");
    }

    [Fact]
    public async Task CapturingCollectionKeepsOriginalBytesAndPrimaryFontDeterminesFamily()
    {
        var font = ApplicationFontFixture.Collection();
        var bundle = new FontFamilyBundle("Noto Sans", [font]);
        var catalog = new SystemFontCatalog([bundle], SystemFontCatalog.Empty);
        var source = new FontFamilySource(() => catalog);

        var captured = await source.CaptureAsync("Noto Sans");
        var primary = await source.CaptureAsync("Unrelated old display name", ApplicationFontFixture.Read());

        Assert.Equal(font.Sha256, Assert.Single(captured.Files).Sha256);
        Assert.Equal(font.Data, Assert.Single(captured.Files).Data);
        Assert.Equal("Noto Sans", primary.FamilyName);
        Assert.Equal(2, primary.Files.Length);
    }

    [Fact]
    public void ValidatorRejectsDeclaredFamilyThatIsAbsentFromOriginalFiles()
    {
        var validator = new FontFamilyValidator();
        validator.Validate(ApplicationFontFixture.Latin());

        Assert.Throws<InvalidDataException>(() => validator.Validate(new("Missing family", [ApplicationFontFixture.Read()])));
        Assert.Throws<InvalidDataException>(() => validator.Validate(new("Noto Sans", [ApplicationFontFixture.Create("bad.ttf", [0, 1, 2])])));
    }

    [MacSystemFontFact]
    public async Task InstalledFamilyCapturePreservesTheOriginalCollection()
    {
        var catalog = new SystemFontCatalog(SKFontManager.Default, ["Helvetica"]);
        var source = new InstalledFontFamilySource(catalog);

        var family = await source.CaptureAsync("Helvetica");

        var file = Assert.Single(family.Files);
        Assert.Equal("Helvetica", family.FamilyName);
        Assert.True(file.Data.AsSpan(0, 4).SequenceEqual("ttcf"u8));
        Assert.Equal(6, OpenTypeFontFileReader.Read(file).Length);
        Assert.Equal(6, new SystemFontCatalog([family], SystemFontCatalog.Empty).Faces.Length);
    }

    [MacSystemFontFact]
    public async Task UnrelatedBrokenFileDoesNotPreventCompleteInstalledFamilyCapture()
    {
        var directory = Directory.CreateTempSubdirectory("aegi-font-source-");
        try
        {
            var broken = Path.Combine(directory.FullName, "unrelated.ttf");
            await File.WriteAllBytesAsync(broken, [0, 1, 2]);
            var catalog = new SystemFontCatalog(SKFontManager.Default, ["Helvetica"]);
            var original = MacFontFileLocator.GetPath("Helvetica")!;
            var source = new InstalledFontFamilySource(catalog, _ => [broken, original]);

            var family = await source.CaptureAsync("Helvetica");

            Assert.Equal(6, OpenTypeFontFileReader.Read(Assert.Single(family.Files)).Length);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [MacSystemFontFact]
    public async Task PartialInstalledFamilyCaptureFailsInsteadOfExportingOnlyOneFace()
    {
        var directory = Directory.CreateTempSubdirectory("aegi-font-partial-");
        try
        {
            var original = ApplicationFontFixture.Create("Helvetica.ttc", await File.ReadAllBytesAsync(MacFontFileLocator.GetPath("Helvetica")!));
            var prepared = PreparedFontFace.Create(original, 0, System.Collections.Immutable.ImmutableDictionary<uint, float>.Empty);
            var partial = Path.Combine(directory.FullName, "Helvetica.ttf");
            await File.WriteAllBytesAsync(partial, prepared.ToArray());
            var catalog = new SystemFontCatalog(SKFontManager.Default, ["Helvetica"]);
            var source = new InstalledFontFamilySource(catalog, _ => [partial]);

            await Assert.ThrowsAsync<InvalidDataException>(() => source.CaptureAsync("Helvetica"));
        }
        finally
        {
            directory.Delete(true);
        }
    }
}
