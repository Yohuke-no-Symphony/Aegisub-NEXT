using System.Collections.Immutable;
using AegiNext.Core.Fonts;
using AegiNext.Application.Tasks;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Rendering.Fonts;

namespace AegiNext.Desktop.Editing;

internal sealed class SubtitleFontSelectionService
{
    private ImmutableArray<SystemFontFace> faces;
    private readonly Func<Task<SystemFontCatalog>>? loadCatalog;
    private readonly Lock loadingGate = new();
    private Task? loading;
    private SystemFontCatalog? systemCatalog;
    private ImmutableArray<FontFamilyBundle> applicationFonts = [];

    internal SubtitleFontSelectionService(AegiTaskService tasks) : this(() => tasks.Submit(new EnumerateSystemFontsTask()).Completion)
    {
    }

    internal SubtitleFontSelectionService(Func<Task<SystemFontCatalog>> loadCatalog) : this(SystemFontCatalog.Empty)
    {
        ArgumentNullException.ThrowIfNull(loadCatalog);
        this.loadCatalog = loadCatalog;
    }

    internal event EventHandler? Changed;

    internal Task EnsureLoadedAsync()
    {
        lock (loadingGate)
        {
            if (loading is null || loading.IsCanceled || loading.IsFaulted)
            {
                loading = LoadAsync();
            }
            return loading;
        }
    }

    private async Task LoadAsync()
    {
        if (loadCatalog is null)
        {
            return;
        }
        var catalog = await loadCatalog();
        systemCatalog = catalog;
        Publish(applicationFonts.IsEmpty ? catalog : new(applicationFonts, catalog));
    }

    internal void SetApplicationFonts(ImmutableArray<FontFamilyBundle> value)
    {
        if (applicationFonts == value)
        {
            return;
        }
        applicationFonts = value;
        if (Catalog is not null)
        {
            Publish(new(value, systemCatalog ?? SystemFontCatalog.Empty));
        }
    }

    private void Publish(SystemFontCatalog catalog)
    {
        Catalog = catalog;
        LoadFaces(catalog.Faces);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    internal SubtitleFontSelectionService(SystemFontCatalog catalog) : this(catalog.Faces)
    {
        Catalog = catalog;
        systemCatalog = catalog;
    }

    internal SubtitleFontSelectionService(IEnumerable<SystemFontFace> faces)
    {
        LoadFaces(faces);
    }

    private void LoadFaces(IEnumerable<SystemFontFace> faces)
    {
        this.faces = faces.ToImmutableArray();
        var candidates = this.faces.GroupBy(face => face.FamilyName, StringComparer.OrdinalIgnoreCase)
            .SelectMany(family => new[]
            {
                new FontPickerCandidate(new(family.Key, isSystemFont: true), family.SelectMany(face => face.Aliases))
            }.Concat(family.Select(face => new FontPickerCandidate(new(face.FamilyName, face.Variant, true), face.Aliases))))
            .ToImmutableArray();
        Candidates = FontSelectionResolver.NormalizeCandidates(candidates).ToImmutableArray();
    }

    internal SystemFontCatalog? Catalog { get; private set; }
    internal ImmutableArray<SystemFontFace> Faces => faces;
    internal IFontNamePreviewProvider? PreviewProvider { get; set; }
    internal ImmutableArray<FontPickerCandidate> Candidates { get; private set; }

    internal static FontSelection FromStyle(SubtitleStyle style) => new(style.FontFamily, style.FontVariant, !style.FontAssetId.HasValue);

    internal FontSelection Resolve(string text, SubtitleStyle current)
    {
        if (!FontSelectionResolver.TryResolve(Candidates, FromStyle(current), text, out var selection))
        {
            throw new InvalidDataException("字体名称无效。");
        }
        return selection;
    }

    internal static SubtitleInlineStyleOverride CreateOverride(FontSelection selection) => new()
    {
        FontFamily = selection.FamilyName, FontVariant = selection.Variant, ClearFontVariant = selection.Variant is null,
        ClearFontAsset = true,
        Bold = selection.Variant is { } variant ? variant.Weight >= 700 : null,
        Italic = selection.Variant?.Italic
    };

    internal SubtitleInlineStyleOverride ToggleBold(SubtitleStyle style, bool bold) => Format(style, bold ? 700 : 400, bold, style.Italic);

    internal SubtitleInlineStyleOverride ToggleItalic(SubtitleStyle style, bool italic) =>
        Format(style, style.FontVariant?.Weight ?? (style.Bold ? 700 : 400), style.Bold, italic);

    private SubtitleInlineStyleOverride Format(SubtitleStyle style, int weight, bool bold, bool italic)
    {
        if (style.FontAssetId.HasValue)
        {
            return new() { Bold = bold, Italic = italic, ClearFontVariant = true };
        }
        var width = style.FontVariant?.Width ?? 5;
        var matching = faces.Where(face => face.Variant.Weight == weight && face.Variant.Width == width &&
                (string.Equals(face.FamilyName, style.FontFamily, StringComparison.OrdinalIgnoreCase) ||
                 face.Aliases.Contains(style.FontFamily, StringComparer.OrdinalIgnoreCase)))
            .OrderBy(face => face.Variant.Italic != italic).ThenBy(face => face.IsVariable).FirstOrDefault();
        return new()
        {
            FontVariant = matching?.Variant, ClearFontVariant = matching is null, Bold = bold, Italic = italic,
            ClearFontAsset = matching is not null
        };
    }
}
