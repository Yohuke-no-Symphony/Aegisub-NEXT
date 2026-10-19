using AegiNext.Core.Fonts;

namespace AegiNext.Application.Tests.Presets;

internal sealed class TestFontFamilyValidator : IFontFamilyValidator
{
    public void Validate(FontFamilyBundle family)
    {
        FontFamilyBundleValidator.Validate(family);
    }
}
