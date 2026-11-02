using System.Collections.Immutable;
using System.Text;
using AegiNext.Core.Presets;
using AegiNext.SubtitleFormats;

namespace AegiNext.Application.Presets;

internal static class AssStylePresetImporter
{
    private static readonly UTF8Encoding strictUtf8 = new(false, true);

    internal static async Task<ImmutableArray<EffectLibraryImportEntry>> LoadAsync(string path, CancellationToken cancellationToken)
    {
        var sourceName = Path.GetFileName(path);
        try
        {
            var bytes = await PresetFileReader.ReadAsync(path, SubtitleStylePresetMetadataStore.MAXIMUM_FILE_BYTES, cancellationToken).ConfigureAwait(false);
            var source = strictUtf8.GetString(bytes);
            cancellationToken.ThrowIfCancellationRequested();
            var parsed = AssSubtitleFormat.ParseStyles(source);
            cancellationToken.ThrowIfCancellationRequested();
            var entries = ImmutableArray.CreateBuilder<EffectLibraryImportEntry>(parsed.Styles.Length);
            foreach (var imported in parsed.Styles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var preset = new SubtitleStylePreset(Guid.NewGuid(), imported.Name, imported.Style);
                try
                {
                    SubtitleStylePresetValidator.Validate(preset);
                }
                catch (InvalidDataException error)
                {
                    throw new InvalidDataException($"ASS 样式“{imported.Name}”无效：{error.Message}", error);
                }
                entries.Add(new()
                {
                    Kind = EffectLibraryItemKind.STYLE,
                    Style = preset,
                    ExternalId = null,
                    SourceName = sourceName,
                    FontFamilies = [],
                    Diagnostics = parsed.Diagnostics.AddRange(imported.Diagnostics)
                });
            }
            return entries.MoveToImmutable();
        }
        catch (Exception error) when (error is InvalidDataException or DecoderFallbackException)
        {
            throw new InvalidDataException($"无法从 ASS 文件“{sourceName}”导入样式：{error.Message}", error);
        }
    }
}
