using AegiNext.Rendering.Fonts;
using Xunit.Abstractions;

namespace AegiNext.Rendering.Tests;

public sealed class InstalledFontFamilySourceTests(ITestOutputHelper output)
{
    [MacSystemFontFact]
    public async Task ActualSystemCatalogCapturesGenericAndCommonFamilies()
    {
        var catalog = new SystemFontCatalog();
        var source = new InstalledFontFamilySource(catalog);
        var families = new[] { "sans-serif", "serif", "monospace", "Helvetica", "Times", "Courier", "Arial", "PingFang SC", "Noto Sans SC" };
        var failures = new List<string>();
        foreach (var name in families)
        {
            if (name == "Noto Sans SC" && !catalog.InstalledFaces.Any(face => FontFamilyCapture.Matches(face, name)))
            {
                output.WriteLine("Noto Sans SC is not installed in the actual system catalog.");
                continue;
            }
            try
            {
                var resolved = new SystemFontResolver(catalog).Resolve(new() { FontFamily = name });
                using (var typeface = resolved.Typeface)
                {
                    output.WriteLine($"{name} resolves to {typeface.FamilyName} ({typeface.PostScriptName}); catalog issues: {string.Join(", ", catalog.Issues.Where(issue => issue.FamilyName == typeface.FamilyName))}");
                }
                var family = await source.CaptureAsync(name);
                output.WriteLine($"{name} -> {family.FamilyName}: {string.Join(", ", family.Files.Select(file => $"{file.FileName} ({file.Data.Length} bytes)"))}");
                new FontFamilyValidator().Validate(family);
                Assert.NotEmpty(family.Files);
            }
            catch (Exception error)
            {
                failures.Add($"{name}: {error}");
            }
        }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [MacSystemFontFact]
    public async Task MissingInstalledFamilyCannotCaptureTheNativeFallbackFont()
    {
        var source = new InstalledFontFamilySource(SystemFontCatalog.Empty);

        await Assert.ThrowsAsync<InvalidDataException>(() => source.CaptureAsync("AegiNext Missing Font Family"));
    }

    [MacSourceHanFontFact]
    public async Task ActualSourceHanFamiliesPreserveEveryVisibleVariantAndOriginalSource()
    {
        var catalog = new SystemFontCatalog();
        var candidates = catalog.InstalledFaces.Where(face => MacSourceHanFontFactAttribute.Matches(face.FamilyName) ||
            face.Aliases.Any(MacSourceHanFontFactAttribute.Matches)).ToArray();
        Assert.NotEmpty(candidates);
        var source = new InstalledFontFamilySource(catalog);
        foreach (var group in candidates.GroupBy(face => face.FamilyName, StringComparer.OrdinalIgnoreCase))
        {
            output.WriteLine($"{group.Key}; aliases: {string.Join(", ", group.SelectMany(face => face.Aliases).Distinct(StringComparer.OrdinalIgnoreCase))}");
            foreach (var face in group.DistinctBy(face => (face.SourceFamilyName, face.StyleIndex)))
            {
                output.WriteLine($"{face.Variant.Name}, {face.Variant.PostScriptName}: {string.Join(", ", SystemFontFilePaths.Get([face]))}");
            }
            var family = await source.CaptureAsync(group.Key);
            output.WriteLine($"Original files: {string.Join(", ", family.Files.Select(file => $"{file.FileName} ({file.Data.Length} bytes)"))}");
            new FontFamilyValidator().Validate(family);
            Assert.NotEmpty(family.Files);
        }
    }
}
