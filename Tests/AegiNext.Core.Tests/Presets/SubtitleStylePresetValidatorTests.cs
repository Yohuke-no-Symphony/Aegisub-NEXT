using System.Collections.Immutable;
using System.Security.Cryptography;
using AegiNext.Core.Presets;
using AegiNext.Core.Fonts;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Tests.Presets;

public sealed class SubtitleStylePresetValidatorTests
{
    [Fact]
    public void OptionalTimingAssociationsValidateWithoutChangingPortableStyleIdentity()
    {
        var preset = new SubtitleStylePreset(Guid.NewGuid(), "Valid", new());
        SubtitleStylePresetValidator.Validate(preset);
        var bound = preset with { TimingPostProcessor = new() };
        SubtitleStylePresetValidator.Validate(bound);
        Assert.Equal(preset.Id, bound.Id);
        Assert.Equal(preset.Style, bound.Style);
        var invalid = bound with { TimingPostProcessor = new TimingPostProcessorOptions { LeadInMilliseconds = -1 } };
        var error = Assert.Throws<InvalidDataException>(() => SubtitleStylePresetValidator.Validate(invalid));
        Assert.IsType<ArgumentOutOfRangeException>(error.InnerException);
        Assert.Throws<InvalidDataException>(() => SubtitleStylePresetValidator.Validate(
            bound with { TimingPostProcessor = new() { BiasPercent = 101 } }));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" leading")]
    [InlineData("trailing ")]
    [InlineData("line\nbreak")]
    [InlineData("e\u0301")]
    public void NonCanonicalNamesAreRejected(string name)
    {
        Assert.Throws<InvalidDataException>(() => SubtitleStylePresetValidator.Validate(new SubtitleStylePreset(Guid.NewGuid(), name, new())));
    }

    [Fact]
    public void UnpairedSurrogateNameIsRejectedBeforeJsonEncoding()
    {
        var name = "bad" + (char)0xd800;
        Assert.Throws<InvalidDataException>(() => SubtitleStylePresetValidator.Validate(new SubtitleStylePreset(Guid.NewGuid(), name, new())));
    }

    [Fact]
    public void DuplicateIdsAndCaseInsensitiveNamesAreRejected()
    {
        var first = new SubtitleStylePreset(Guid.NewGuid(), "Title", new());
        Assert.Throws<InvalidDataException>(() => SubtitleStylePresetValidator.Validate(new SubtitleStylePresetCollection { Presets = [first, first with { Name = "Another" }] }));
        Assert.Throws<InvalidDataException>(() => SubtitleStylePresetValidator.Validate(new SubtitleStylePresetCollection { Presets = [first, first with { Id = Guid.NewGuid(), Name = "TITLE" }] }));
    }

    [Fact]
    public void PortableStyleRejectsProjectFontReferencesInvalidStylesAndUnsafeFontNames()
    {
        var preset = new SubtitleStylePreset(Guid.NewGuid(), "Valid", new());
        SubtitleStylePresetValidator.Validate(preset);
        Assert.Throws<InvalidDataException>(() => SubtitleStylePresetValidator.Validate(preset with { Style = preset.Style with { FontAssetId = Guid.NewGuid() } }));
        Assert.Throws<InvalidDataException>(() => SubtitleStylePresetValidator.Validate(preset with { Style = preset.Style with { FontSize = double.NaN } }));
        var bytes = ImmutableArray.Create<byte>(1, 2, 3);
        var font = new EmbeddedSubtitleFont("../font.ttf", Convert.ToHexStringLower(SHA256.HashData(bytes.AsSpan())), bytes);
        Assert.Throws<InvalidDataException>(() => FontFamilyBundleValidator.ValidateFont(font));
        Assert.Throws<InvalidDataException>(() => FontFamilyBundleValidator.ValidateFont(font with { FileName = "font.ttf", Sha256 = new string('0', 64) }));
    }

    [Theory]
    [InlineData("font.ttf.")]
    [InlineData("FONT:bad.ttf")]
    [InlineData("CON.ttf")]
    [InlineData("font.exe")]
    [InlineData("font\\name.ttf")]
    public void EmbeddedFontNamesMustRemainPortable(string fileName)
    {
        var bytes = ImmutableArray.Create<byte>(1, 2, 3);
        var font = new EmbeddedSubtitleFont(fileName, Convert.ToHexStringLower(SHA256.HashData(bytes.AsSpan())), bytes);
        Assert.Throws<InvalidDataException>(() => FontFamilyBundleValidator.ValidateFont(font));
    }

    [Fact]
    public void EmptyPayloadDefaultCollectionsAndExcessivePresetCountAreRejected()
    {
        var empty = new EmbeddedSubtitleFont("font.ttf", Convert.ToHexStringLower(SHA256.HashData([])), []);
        Assert.Throws<InvalidDataException>(() => FontFamilyBundleValidator.ValidateFont(empty));
        Assert.Throws<InvalidDataException>(() => SubtitleStylePresetValidator.Validate(new SubtitleStylePresetCollection { Presets = default }));
        var many = Enumerable.Range(0, 257).Select(index => new SubtitleStylePreset(Guid.NewGuid(), $"Style {index}", new())).ToImmutableArray();
        Assert.Throws<InvalidDataException>(() => SubtitleStylePresetValidator.Validate(new SubtitleStylePresetCollection { Presets = many }));
    }

    [Fact]
    public void LibraryFontValidationStillRejectsDefaultCollectionsAndDuplicateFamilyNames()
    {
        var bytes = ImmutableArray.Create<byte>(1, 2, 3);
        var font = new EmbeddedSubtitleFont("font.ttf", Convert.ToHexStringLower(SHA256.HashData(bytes.AsSpan())), bytes);
        var family = new FontFamilyBundle("Example", [font]);
        FontFamilyBundleValidator.ValidateLibrary([family]);
        Assert.Throws<InvalidDataException>(() => FontFamilyBundleValidator.ValidateLibrary(default(ImmutableArray<FontFamilyBundle>)));
        Assert.Throws<InvalidDataException>(() => FontFamilyBundleValidator.ValidateLibrary([family, family with { FamilyName = "EXAMPLE" }]));
        Assert.Throws<InvalidDataException>(() => FontFamilyBundleValidator.Validate(default(ImmutableArray<FontFamilyBundle>)));
    }
}
