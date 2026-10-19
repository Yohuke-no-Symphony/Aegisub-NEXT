using AegiNext.Core.Projects;
using SkiaSharp;

namespace AegiNext.Rendering.Fonts;

internal sealed class SystemFontResolver
{
    private readonly SystemFontCatalog catalog;
    private readonly SKFontManager manager;

    internal SystemFontResolver(SystemFontCatalog catalog, SKFontManager? manager = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        this.catalog = catalog;
        this.manager = manager ?? SKFontManager.Default;
    }

    internal ResolvedSystemFont Resolve(SubtitleStyle style)
    {
        if (style.FontVariant is not { } variant)
        {
            var application = OrderCandidates(catalog.Faces.Where(face => face.IsApplicationFont && MatchesFamily(face, style.FontFamily)), style)
                .FirstOrDefault();
            if (application is not null)
            {
                return TryOpen(application)!.Value with { IsExactMatch = true };
            }
            using var fontStyle = FontStyle(style);
            return new(SKTypeface.FromFamilyName(style.FontFamily, fontStyle), null, false);
        }
        var exact = Match(style.FontFamily, variant);
        if (exact is not null && TryOpen(exact) is { } resolved)
        {
            return resolved with { IsExactMatch = true };
        }
        var familyFaces = catalog.Faces.Where(face => MatchesFamily(face, style.FontFamily));
        foreach (var candidate in familyFaces.OrderBy(face => face.Variant.Italic != style.Italic)
                     .ThenBy(face => Math.Abs(face.Variant.Width - variant.Width))
                     .ThenBy(face => Math.Abs(face.Variant.Weight - variant.Weight))
                     .ThenBy(face => face.IsVariable))
        {
            if (candidate == exact)
            {
                continue;
            }
            if (TryOpen(candidate) is { } fallback)
            {
                return fallback;
            }
        }
        using var requestedStyle = FontStyle(style);
        var typeface = manager.MatchFamily(style.FontFamily, requestedStyle)
            ?? SKTypeface.FromFamilyName(style.FontFamily, requestedStyle)
            ?? SKTypeface.CreateDefault();
        return new(typeface, Describe(typeface), false);
    }

    internal SystemFontFace? Match(string familyName, SubtitleFontVariant variant)
    {
        if (variant.PostScriptName is { } postScriptName)
        {
            var privateFamily = catalog.Faces.Any(face => face.IsApplicationFont && MatchesFamily(face, familyName));
            var byPostScript = catalog.Faces.FirstOrDefault(face => (!privateFamily || face.IsApplicationFont && MatchesFamily(face, familyName)) &&
                string.Equals(face.Variant.PostScriptName, postScriptName, StringComparison.OrdinalIgnoreCase));
            if (byPostScript is not null)
            {
                return byPostScript;
            }
        }
        return catalog.Faces.FirstOrDefault(face => MatchesFamily(face, familyName)
            && NormalizeName(face.Variant.Name) == NormalizeName(variant.Name)
            && face.Variant.Weight == variant.Weight && face.Variant.Width == variant.Width && face.Variant.Italic == variant.Italic);
    }

    internal IEnumerable<ResolvedSystemFont> ApplicationFallbacks(SubtitleStyle style)
    {
        var candidates = OrderCandidates(catalog.Faces.Where(face => face.IsApplicationFont), style)
            .GroupBy(face => face.FamilyName, StringComparer.OrdinalIgnoreCase).Select(family => family.First());
        foreach (var candidate in candidates)
        {
            yield return TryOpen(candidate)!.Value;
        }
    }

    internal IEnumerable<ResolvedSystemFont> SystemFallbacks(SubtitleStyle style, int character)
    {
        using var requestedStyle = FontStyle(style);
        foreach (var family in new[] { style.FontFamily, null })
        {
            var typeface = manager.MatchCharacter(family, requestedStyle, ["und"], character);
            if (typeface is null)
            {
                continue;
            }
            if (catalog.Faces.Any(face => face.IsApplicationFont && MatchesFamily(face, typeface.FamilyName)))
            {
                typeface.Dispose();
                continue;
            }
            yield return new(typeface, style.FontVariant.HasValue ? Describe(typeface) : null, false);
        }
        var candidates = OrderCandidates(catalog.Faces.Where(face => !face.IsApplicationFont), style)
            .GroupBy(face => face.FamilyName, StringComparer.OrdinalIgnoreCase).Select(family => family.First());
        foreach (var candidate in candidates)
        {
            if (TryOpen(candidate) is { } resolved)
            {
                yield return resolved;
            }
        }
    }

    private static IOrderedEnumerable<SystemFontFace> OrderCandidates(IEnumerable<SystemFontFace> faces, SubtitleStyle style)
    {
        return faces.OrderBy(face => face.Variant.Italic != style.Italic)
            .ThenBy(face => Math.Abs(face.Variant.Width - (style.FontVariant?.Width ?? 5)))
            .ThenBy(face => Math.Abs(face.Variant.Weight - (style.FontVariant?.Weight ?? (style.Bold ? 700 : 400))))
            .ThenBy(face => face.IsVariable).ThenBy(face => face.FamilyName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(face => face.Variant.Name, StringComparer.OrdinalIgnoreCase);
    }

    internal static SKFontStyle FontStyle(SubtitleStyle style)
    {
        return new(style.FontVariant?.Weight ?? (style.Bold ? 700 : 400), style.FontVariant?.Width ?? 5,
            style.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
    }

    internal static SystemFontFace? Describe(SKTypeface typeface)
    {
        try
        {
            var faces = SystemFontCatalog.ReadTypeface(typeface, typeface.FamilyName, 0, null);
            var enumerated = faces.FirstOrDefault(face => face.IsStyleEnumerated);
            if (enumerated is not null || typeface.PostScriptName is not { } postScriptName)
            {
                return enumerated;
            }
            return faces.FirstOrDefault(face => face.Variant.PostScriptName is { } candidateName
                && NormalizeName(candidateName) == NormalizeName(postScriptName));
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private ResolvedSystemFont? TryOpen(SystemFontFace candidate)
    {
        if (candidate.ApplicationSource is { } application)
        {
            return new(application.Open(), candidate, false);
        }
        using var styles = manager.GetFontStyles(candidate.SourceFamilyName);
        if (candidate.StyleIndex < 0 || candidate.StyleIndex >= styles.Count)
        {
            return null;
        }
        var typeface = styles.CreateTypeface(candidate.StyleIndex);
        if (typeface is null)
        {
            return null;
        }
        if (candidate.IsStyleEnumerated)
        {
            try
            {
                var nativeFaces = SystemFontCatalog.ReadTypeface(typeface, candidate.SourceFamilyName, candidate.StyleIndex,
                    styles.GetStyleName(candidate.StyleIndex));
                var actual = nativeFaces.FirstOrDefault(face => face.IsStyleEnumerated
                    && face.Variant.Weight == candidate.Variant.Weight && face.Variant.Width == candidate.Variant.Width
                    && face.Variant.Italic == candidate.Variant.Italic
                    && NormalizeName(face.Variant.Name) == NormalizeName(candidate.Variant.Name));
                if (actual is not null)
                {
                    return new(typeface, actual, false);
                }
            }
            catch (InvalidDataException)
            {
            }
            typeface.Dispose();
            return null;
        }
        if (!candidate.IsVariable)
        {
            typeface.Dispose();
            return null;
        }
        using (typeface)
        using (var stream = typeface.OpenStream())
        {
            if (stream is null)
            {
                return null;
            }
            using var data = SKData.Create(stream);
            var namedIndex = checked((candidate.NamedInstanceIndex << 16) | candidate.CollectionIndex);
            var named = SKTypeface.FromData(data, namedIndex);
            if (named is null)
            {
                return null;
            }
            using var namedStream = named.OpenStream(out var actualIndex);
            if (namedStream is not null && actualIndex == namedIndex)
            {
                return new(named, candidate, false);
            }
            named.Dispose();
            return null;
        }
    }

    private static bool MatchesFamily(SystemFontFace face, string familyName)
    {
        return string.Equals(face.FamilyName, familyName, StringComparison.OrdinalIgnoreCase)
            || face.Aliases.Contains(familyName, StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeName(string value)
    {
        return string.Concat(value.Where(char.IsLetterOrDigit)).ToUpperInvariant();
    }
}
