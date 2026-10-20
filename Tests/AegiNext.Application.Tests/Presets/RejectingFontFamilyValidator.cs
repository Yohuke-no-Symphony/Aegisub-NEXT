using AegiNext.Core.Fonts;

namespace AegiNext.Application.Tests.Presets;

internal sealed class RejectingFontFamilyValidator : IFontFamilyValidator
{
    internal int ValidateCount { get; private set; }

    public void Validate(FontFamilyBundle family)
    {
        ValidateCount++;
        throw new InvalidDataException("此操作不应验证字体。");
    }
}
