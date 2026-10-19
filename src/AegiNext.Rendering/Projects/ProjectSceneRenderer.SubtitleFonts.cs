using System.Collections.Immutable;
using System.Globalization;
using AegiNext.Core.Projects;
using AegiNext.Rendering.Fonts;
using SkiaSharp;

namespace AegiNext.Rendering.Projects;

public sealed partial class ProjectSceneRenderer
{
    private void ShapeFontRuns(ProjectDocument document, SubtitleStyle style, string text, int offset,
        TextDirection direction, ImmutableArray<SubtitleLayoutRun>.Builder runs)
    {
        if (style.FontAssetId.HasValue)
        {
            AddFontRun(GetPreferredTextShaper(document, style), style, text, offset, direction, runs);
            return;
        }
        var boundaries = StringInfo.ParseCombiningCharacters(text);
        var begin = 0;
        var firstEnd = boundaries.Length > 1 ? boundaries[1] : text.Length;
        var current = GetSystemTextShaper(document, style, text[..firstEnd]);
        for (var index = 1; index < boundaries.Length; index++)
        {
            var start = boundaries[index];
            var end = index + 1 < boundaries.Length ? boundaries[index + 1] : text.Length;
            var next = GetSystemTextShaper(document, style, text[start..end]);
            if (ReferenceEquals(current, next))
            {
                continue;
            }
            AddFontRun(current, style, text[begin..start], offset + begin, direction, runs);
            begin = start;
            current = next;
        }
        AddFontRun(current, style, text[begin..], offset + begin, direction, runs);
    }

    private static void AddFontRun(TextShaper shaper, SubtitleStyle style, string text, int offset,
        TextDirection direction, ImmutableArray<SubtitleLayoutRun>.Builder runs)
    {
        var shape = shaper.Shape(text, (float)style.FontSize, direction, "und", (float)style.LetterSpacing);
        runs.Add(new(text, offset, style, shape, direction)
        {
            ResolvedFontFamily = shaper.FontFamily, ResolvedFontVariant = shaper.ResolvedFontVariant
        });
    }

    private TextShaper GetPreferredTextShaper(ProjectDocument document, SubtitleStyle style)
    {
        var key = (style.FontAssetId, style.FontFamily, style.FontVariant, style.Bold, style.Italic);
        if (textShapers.TryGetValue(key, out var existing))
        {
            return existing;
        }
        var resolved = !style.FontAssetId.HasValue && (style.FontVariant.HasValue || hasApplicationFonts)
            ? systemFonts.Value.Resolve(style) : new ResolvedSystemFont(Typeface(document, style), null, false);
        var result = CacheTextShaper(resolved.Typeface, style, resolved.Face);
        textShapers.Add(key, result);
        return result;
    }

    private TextShaper GetSystemTextShaper(ProjectDocument document, SubtitleStyle style, string grapheme)
    {
        var key = (style.FontFamily, style.FontVariant, style.Bold, style.Italic, grapheme);
        if (resolvedTextShapers.TryGetValue(key, out var existing))
        {
            return existing;
        }
        var preferred = GetPreferredTextShaper(document, style);
        if (preferred.ContainsGlyphs(grapheme))
        {
            resolvedTextShapers.Add(key, preferred);
            return preferred;
        }
        if (hasApplicationFonts)
        {
            foreach (var candidate in systemFonts.Value.ApplicationFallbacks(style))
            {
                var fallback = CacheTextShaper(candidate.Typeface, style, candidate.Face);
                if (fallback.ContainsGlyphs(grapheme))
                {
                    resolvedTextShapers.Add(key, fallback);
                    return fallback;
                }
            }
        }
        using var fontStyle = FontStyle(style);
        foreach (var rune in grapheme.EnumerateRunes())
        {
            if (TextShaper.IsShapingControl(rune))
            {
                continue;
            }
            if (hasApplicationFonts)
            {
                foreach (var systemCandidate in systemFonts.Value.SystemFallbacks(style, rune.Value))
                {
                    var systemFallback = CacheTextShaper(systemCandidate.Typeface, style, systemCandidate.Face);
                    if (systemFallback.ContainsGlyphs(grapheme))
                    {
                        resolvedTextShapers.Add(key, systemFallback);
                        return systemFallback;
                    }
                }
                continue;
            }
            var candidate = SKFontManager.Default.MatchCharacter(style.FontFamily, fontStyle, ["und"], rune.Value);
            if (candidate is null)
            {
                continue;
            }
            var fallback = CacheTextShaper(candidate, style,
                style.FontVariant.HasValue ? SystemFontResolver.Describe(candidate) : null);
            if (fallback.ContainsGlyphs(grapheme))
            {
                resolvedTextShapers.Add(key, fallback);
                return fallback;
            }
        }
        throw new InvalidDataException($"No system font covers the grapheme '{grapheme}'; import a project font asset.");
    }

    private TextShaper CacheTextShaper(SKTypeface typeface, SubtitleStyle style, SystemFontFace? face = null)
    {
        var key = (typeface.Handle, style.FontVariant, face?.NamedInstanceIndex ?? 0, face?.CollectionIndex ?? 0, style.Bold, style.Italic);
        if (actualTextShapers.TryGetValue(key, out var existing))
        {
            if (!existing.UsesTypeface(typeface))
            {
                typeface.Dispose();
            }
            return existing;
        }
        var result = new TextShaper(typeface, style.Bold, style.Italic, face, style.FontVariant.HasValue && !style.FontAssetId.HasValue);
        actualTextShapers.Add(key, result);
        return result;
    }

    private static SKFontStyle FontStyle(SubtitleStyle style)
    {
        return SystemFontResolver.FontStyle(style);
    }
}
