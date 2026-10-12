using AegiNext.Export.Worker;
using System.Globalization;
using System.Text;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Media.Probing;
using AegiNext.SubtitleFormats;

namespace AegiNext.Export.Muxing;

internal static class SubtitleMuxer
{
    internal static async Task<SubtitleMuxReceipt> RunAsync(SubtitleMuxRequest request,
        IProgress<SubtitleMuxProgress>? progress, CancellationToken cancellationToken)
    {
        SubtitleMuxRequestValidator.Validate(request);
        progress?.Report(new(MediaTime.Zero, null, "preflight"));
        var ffmpeg = ExportExecutable.Resolve("ffmpeg", request.FfmpegPath);
        await MediaToolchain.ValidateFfmpegAsync(ffmpeg, cancellationToken).ConfigureAwait(false);
        var probe = new FfprobeMediaProbe(new(Path.Combine(Path.GetDirectoryName(ffmpeg)!,
            OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe")));
        var binding = request.Project.Media!;
        var asset = request.Project.Assets.Single(item => item.Id == binding.AssetId);
        var sourcePath = ProjectAssetLocation.Resolve(asset, request.ProjectDirectory);
        var source = await probe.ProbeAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        if (source.Asset.Streams.SingleOrDefault(stream => stream.Index == binding.VideoStreamIndex)?.Video is null)
        {
            throw new InvalidDataException("绑定的源视频流不存在或不是视频。");
        }

        var earliest = request.Project.Subtitles.Min(line => line.Start) + binding.MediaOrigin;
        var subtitleOrigin = earliest < MediaTime.Zero ? earliest : MediaTime.Zero;
        var text = SubtitleTextFormat.WriteSrt(request.Project.Subtitles, binding.MediaOrigin - subtitleOrigin);
        var subtitlePath = Path.Combine(Path.GetDirectoryName(request.OutputPath)!, "subtitles.srt");
        await File.WriteAllTextAsync(subtitlePath, text, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
        var ffprobe = Path.Combine(Path.GetDirectoryName(ffmpeg)!, OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
        var chapters = await SubtitleMuxVerification.ReadChaptersAsync(ffprobe, sourcePath, cancellationToken).ConfigureAwait(false);
        var chapterCarriers = await SubtitleMuxVerification.FindChapterCarriersAsync(ffprobe, source, chapters.Length, cancellationToken).ConfigureAwait(false);
        var streams = source.Asset.Streams.Where(stream => !chapterCarriers.Contains(stream.Index)).OrderBy(stream => stream.Index).ToArray();
        var subtitleOrdinal = streams.Count(stream => stream.CodecType == "subtitle");
        var codec = SubtitleMuxCapabilities.GetSubtitleCodec(Path.GetExtension(request.OutputPath));
        var arguments = new List<string>
        {
            "-nostdin", "-v", "error", "-nostats", "-progress", "pipe:1", "-copyts", "-i", sourcePath,
            "-itsoffset", Seconds(subtitleOrigin), "-f", "srt", "-i", subtitlePath
        };
        foreach (var stream in streams)
        {
            arguments.AddRange(["-map", $"0:{stream.Index.ToString(CultureInfo.InvariantCulture)}"]);
        }

        arguments.AddRange(["-map", "1:0", "-c", "copy", $"-c:s:{subtitleOrdinal.ToString(CultureInfo.InvariantCulture)}",
            codec == "subrip" ? "copy" : codec, "-map_metadata", "0", "-map_chapters", "0", "-avoid_negative_ts", "disabled"]);
        if (codec == "mov_text" && streams.Where(stream => stream.Video is not null)
                .All(stream => stream.Video!.Color.Range is "tv" or "pc"))
        {
            arguments.AddRange(["-movflags", "+write_colr"]);
        }

        for (var index = 0; index < streams.Length; index++)
        {
            var disposition = string.Join('+', streams[index].Disposition.Where(pair => pair.Value != 0).Select(pair => pair.Key));
            arguments.AddRange([$"-disposition:{index.ToString(CultureInfo.InvariantCulture)}", disposition.Length == 0 ? "0" : disposition]);
        }

        arguments.AddRange([$"-disposition:{streams.Length.ToString(CultureInfo.InvariantCulture)}", "0", "-n", request.OutputPath]);
        progress?.Report(new(MediaTime.Zero, 0, "muxing"));
        await SubtitleMuxProcess.RunAsync(ffmpeg, arguments, position =>
        {
            var fraction = position > MediaTime.Zero && source.Asset.ReportedDuration is { } duration && duration > MediaTime.Zero
                ? Math.Clamp((double)position.Numerator / position.Denominator /
                    ((double)duration.Numerator / duration.Denominator), 0, 1) : (double?)null;
            progress?.Report(new(position, fraction, "muxing"));
        }, cancellationToken).ConfigureAwait(false);
        progress?.Report(new(MediaTime.Zero, null, "verifying"));
        return await SubtitleMuxVerification.VerifyAsync(request.OutputPath, source, probe, ffmpeg,
            streams, chapters, text, subtitleOrigin, codec, cancellationToken).ConfigureAwait(false);
    }

    internal static string Seconds(MediaTime time) =>
        ((decimal)time.Numerator / time.Denominator).ToString("0.##################", CultureInfo.InvariantCulture);
}
