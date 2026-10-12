using AegiNext.Core.Projects;
using AegiNext.SubtitleFormats;

namespace AegiNext.Application.Tests;

internal sealed class RecordingAssFontWeightResolver(params (string Family, SubtitleFontVariant Variant)[] faces)
    : IAssFontWeightResolver
{
    internal List<(string Family, int Weight, int Width, bool Italic)> Requests { get; } = [];
    internal SubtitleFontVariant? ForcedVariant { get; init; }

    public SubtitleFontVariant? ResolveVariant(string fontFamily, int weight, int width, bool italic)
    {
        Requests.Add((fontFamily, weight, width, italic));
        if (ForcedVariant is { } forced)
        {
            return forced;
        }
        foreach (var face in faces)
        {
            if (string.Equals(face.Family, fontFamily, StringComparison.OrdinalIgnoreCase) &&
                face.Variant.Weight == weight && face.Variant.Width == width && face.Variant.Italic == italic)
            {
                return face.Variant;
            }
        }
        return null;
    }
}
