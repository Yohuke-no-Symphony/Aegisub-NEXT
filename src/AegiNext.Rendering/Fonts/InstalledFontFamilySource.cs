using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using AegiNext.Core.Fonts;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using SkiaSharp;

namespace AegiNext.Rendering.Fonts;

/// <summary>从已安装字体的真实文件捕获完整字体族，不以 Skia 重建字体流替代原始文件。</summary>
public sealed class InstalledFontFamilySource : IFontFamilySource
{
    private readonly SystemFontCatalog? catalog;
    private readonly Func<IEnumerable<SystemFontFace>, IEnumerable<string>> getFilePaths;

    /// <summary>借用可选系统字体快照；省略时在捕获任务中枚举系统字体。</summary>
    public InstalledFontFamilySource(SystemFontCatalog? catalog = null) : this(catalog, SystemFontFilePaths.Get)
    {
    }

    internal InstalledFontFamilySource(SystemFontCatalog? catalog, Func<IEnumerable<SystemFontFace>, IEnumerable<string>> getFilePaths)
    {
        ArgumentNullException.ThrowIfNull(getFilePaths);
        this.catalog = catalog;
        this.getFilePaths = getFilePaths;
    }

    /// <inheritdoc />
    public Task<FontFamilyBundle> CaptureAsync(string familyName, EmbeddedSubtitleFont? primaryFont = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Capture(familyName, primaryFont, cancellationToken), cancellationToken);
    }

    private FontFamilyBundle Capture(string familyName, EmbeddedSubtitleFont? primaryFont, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var target = FontFamilyCapture.ResolvePrimaryFamily(familyName, primaryFont);
        var snapshot = catalog ?? new SystemFontCatalog(SKFontManager.Default, cancellationToken: cancellationToken);
        var candidates = snapshot.InstalledFaces.Where(face => FontFamilyCapture.Matches(face, target)).ToArray();
        if (candidates.Length == 0 && primaryFont is null && IsGenericFamily(target))
        {
            var resolved = new SystemFontResolver(snapshot).Resolve(new SubtitleStyle { FontFamily = target });
            using var typeface = resolved.Typeface;
            target = typeface.FamilyName;
            candidates = snapshot.InstalledFaces.Where(face => FontFamilyCapture.Matches(face, target)).ToArray();
        }
        if (candidates.Length == 0)
        {
            var requested = new SystemFontCatalog(SKFontManager.Default, [target], cancellationToken);
            candidates = requested.InstalledFaces.Where(face => FontFamilyCapture.Matches(face, target)).ToArray();
        }
        if (candidates.Length == 0)
        {
            if (primaryFont is not null)
            {
                return FontFamilyCapture.Create(target, [], primaryFont);
            }
            throw new InvalidDataException($"Installed font family '{familyName}' is unavailable; its original files cannot be exported.");
        }
        var canonical = candidates.FirstOrDefault(face => string.Equals(face.FamilyName, target, StringComparison.OrdinalIgnoreCase))?.FamilyName
            ?? candidates[0].FamilyName;
        var required = ReadInstalledFingerprints(candidates, cancellationToken);
        var captured = new HashSet<string>(StringComparer.Ordinal);
        var files = new List<EmbeddedSubtitleFont>();
        var paths = getFilePaths(candidates).Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = OpenCandidate(path);
            if (stream is null)
            {
                continue;
            }
            ImmutableArray<OpenTypeFontFileFace> matching;
            try
            {
                matching = OpenTypeFontFileReader.Read(stream).Where(face => FontFamilyValidator.MatchesFamily(face.Metadata, canonical)).ToImmutableArray();
            }
            catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                continue;
            }
            if (matching.IsEmpty)
            {
                continue;
            }
            foreach (var face in matching)
            {
                captured.Add(OpenTypeFontFingerprint.Read(stream, face.CollectionIndex, cancellationToken));
            }
            stream.Position = 0;
            var bytes = new byte[checked((int)stream.Length)];
            stream.ReadExactly(bytes);
            var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
            var fileName = Path.GetFileName(path).Normalize(NormalizationForm.FormC);
            if (fileName.Length > 128)
            {
                fileName = digest + Path.GetExtension(path);
            }
            files.Add(new(fileName, digest, ImmutableArray.Create(bytes)));
        }
        if (files.Count == 0 || !required.IsSubsetOf(captured))
        {
            throw new InvalidDataException($"The complete original files of font family '{canonical}' could not be located; partial family export is disabled.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        return FontFamilyCapture.Create(canonical, files, primaryFont);
    }

    private static FileStream? OpenCandidate(string path)
    {
        try
        {
            return new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static HashSet<string> ReadInstalledFingerprints(IEnumerable<SystemFontFace> faces, CancellationToken cancellationToken)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in faces.DistinctBy(face => (face.SourceFamilyName, face.StyleIndex)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var styles = SKFontManager.Default.GetFontStyles(source.SourceFamilyName);
            if (source.StyleIndex < 0 || source.StyleIndex >= styles.Count)
            {
                throw new InvalidDataException("The installed font catalog changed while capturing its original files.");
            }
            using var typeface = styles.CreateTypeface(source.StyleIndex)
                ?? throw new InvalidDataException("An installed font face could not be opened while capturing its original files.");
            result.Add(OpenTypeFontFingerprint.Read(typeface, cancellationToken));
        }
        return result;
    }

    private static bool IsGenericFamily(string name)
    {
        return name.Equals("sans-serif", StringComparison.OrdinalIgnoreCase) || name.Equals("serif", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("monospace", StringComparison.OrdinalIgnoreCase);
    }
}
