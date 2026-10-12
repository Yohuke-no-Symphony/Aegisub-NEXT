using System.Collections.Immutable;
using System.Text;
using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Rendering.Fonts;
using AegiNext.Rendering.SubtitleFormats;
using AegiNext.SubtitleFormats;
using SkiaSharp;

namespace AegiNext.Cli;

/// <summary>复用格式库、工程导入操作和共享 ASS 排版完成转换。</summary>
public sealed class SubtitleConversionService : ISubtitleConversionService
{
    /// <summary>读取严格 UTF-8 字幕或版本化工程，汇总导入和导出诊断。</summary>
    public async Task<SubtitleFormatWriteResult> ConvertAsync(string inputPath, string target, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(inputPath);
        var diagnostics = ImmutableArray<SubtitleFormatDiagnostic>.Empty;
        ProjectDocument document;
        SystemFontCatalog? fonts = null;
        if (extension.Equals(".aeginext", StringComparison.OrdinalIgnoreCase))
        {
            document = await ProjectStore.LoadAsync(inputPath, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var bytes = await File.ReadAllBytesAsync(inputPath, cancellationToken).ConfigureAwait(false);
            var bom = bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }) ? 3 : 0;
            var source = new UTF8Encoding(false, true).GetString(bytes, bom, bytes.Length - bom);
            cancellationToken.ThrowIfCancellationRequested();
            if (extension.Equals(".ass", StringComparison.OrdinalIgnoreCase))
            {
                fonts = new(SKFontManager.Default, cancellationToken: cancellationToken);
                var imported = AssSubtitleFormat.ParseAtSourceResolution(source, new AssFontWeightResolver(fonts.Faces));
                document = ProjectEditingOperations.ImportSubtitleLines(new()
                {
                    Width = imported.Width,
                    Height = imported.Height,
                    Name = Path.GetFileNameWithoutExtension(inputPath)
                }, imported.Result, Path.GetFileNameWithoutExtension(inputPath));
                diagnostics = imported.Result.Diagnostics;
            }
            else if (extension.Equals(".srt", StringComparison.OrdinalIgnoreCase))
            {
                document = ProjectEditingOperations.ImportSubtitleLines(new(), SubtitleTextFormat.ParseSrt(source), Path.GetFileNameWithoutExtension(inputPath));
            }
            else
            {
                throw new CliUsageException("Conversion input must be .aeginext, .ass, or .srt.");
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        var mapping = SubtitleTimelineExchange.GetMapping(document.Media);
        var timeOffset = mapping?.Origin ?? MediaTime.Zero;
        if (!mapping.HasValue)
        {
            diagnostics = diagnostics.Add(new("Subtitle.PlaybackOriginUnknown", "The project media playback origin is unconfirmed; accepting this diagnostic exports subtitles with zero offset."));
        }
        SubtitleFormatWriteResult result;
        if (target.Equals("ass", StringComparison.OrdinalIgnoreCase))
        {
            fonts ??= new(SKFontManager.Default, cancellationToken: cancellationToken);
            using var measurer = new AssSubtitlePlacementMeasurer(Path.GetDirectoryName(inputPath)!, fonts);
            result = AssSubtitleFormat.Write(document, timeOffset, measurer);
        }
        else if (target.Equals("srt", StringComparison.OrdinalIgnoreCase))
        {
            result = new(SubtitleTextFormat.WriteSrt(document.Subtitles, timeOffset), SubtitleFormatLossAnalysis.ForSrt(document));
        }
        else
        {
            throw new CliUsageException("--target must be ass or srt.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        return result with { Diagnostics = diagnostics.AddRange(result.Diagnostics) };
    }
}
