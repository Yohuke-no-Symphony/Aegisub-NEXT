using AegiNext.Export.Worker;
using System.Globalization;
using System.Text.Json;
using AegiNext.Core.Media;
using AegiNext.Core.Timing;
using AegiNext.Media.Probing;
using AegiNext.Media.Processes;
using AegiNext.SubtitleFormats;

namespace AegiNext.Export.Muxing;

internal static class SubtitleMuxVerification
{
    internal static async Task<SubtitleMuxReceipt> VerifyAsync(string outputPath, MediaProbeReport source,
        FfprobeMediaProbe probe, string ffmpeg, MediaStreamInfo[] sourceStreams, string[] sourceChapters,
        string expectedSrt, MediaTime subtitleOrigin,
        string subtitleCodec, CancellationToken cancellationToken)
    {
        var output = await probe.ProbeAsync(outputPath, cancellationToken).ConfigureAwait(false);
        var outputStreams = output.Asset.Streams.OrderBy(stream => stream.Index).ToArray();
        if (outputStreams.Length < sourceStreams.Length + 1)
        {
            throw new InvalidDataException("封装后缺少源流或新增字幕；未提交输出。");
        }

        var ffprobe = Path.Combine(Path.GetDirectoryName(ffmpeg)!, OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
        var outputChapters = await ReadChaptersAsync(ffprobe, outputPath, cancellationToken).ConfigureAwait(false);
        var outputCarriers = await FindChapterCarriersAsync(ffprobe, output, outputChapters.Length, cancellationToken).ConfigureAwait(false);
        if (!sourceChapters.SequenceEqual(outputChapters, StringComparer.Ordinal) ||
            outputStreams.Skip(sourceStreams.Length + 1).Any(stream => !outputCarriers.Contains(stream.Index)))
        {
            throw new InvalidDataException("封装后章节或额外流校验失败；未提交输出。");
        }

        for (var index = 0; index < sourceStreams.Length; index++)
        {
            var original = sourceStreams[index];
            var copied = outputStreams[index];
            ValidateCopiedStream(original, copied);
            if (original.CodecType is "video" or "audio" or "subtitle")
            {
                await ValidatePacketStartAsync(ffprobe, source.SourcePath, original, outputPath, copied, cancellationToken).ConfigureAwait(false);
            }
            if (original.CodecType == "audio")
            {
                await ValidateAudioPacketsAsync(ffprobe, source.SourcePath, original, outputPath, copied, cancellationToken).ConfigureAwait(false);
            }
        }

        var subtitle = outputStreams[sourceStreams.Length];
        var allowEnabledSubtitle = subtitleCodec == "mov_text" && sourceStreams.All(stream => stream.CodecType != "subtitle");
        if (subtitle.CodecType != "subtitle" || subtitle.CodecName != subtitleCodec ||
            subtitle.Disposition.Any(pair => pair.Value != 0 &&
                !(allowEnabledSubtitle && pair.Key == "default" && pair.Value == 1)))
        {
            throw new InvalidDataException("新增字幕的编码或播放偏好校验失败；未提交输出。");
        }

        var extracted = await MediaToolProcess.RunAsync(ffmpeg,
            ["-nostdin", "-v", "error", "-copyts", "-itsoffset", SubtitleMuxer.Seconds(-subtitleOrigin), "-i", outputPath,
                "-map", $"0:{subtitle.Index.ToString(CultureInfo.InvariantCulture)}", "-c:s", "srt", "-f", "srt", "pipe:1"],
            TimeSpan.FromHours(24), checked(Math.Max(65536, expectedSrt.Length * 8 + 65536)), 65536, cancellationToken).ConfigureAwait(false);
        if (extracted.ExitCode != 0)
        {
            throw new InvalidDataException($"无法验证新增字幕；未提交输出。{extracted.StandardError}");
        }

        ValidateSubtitles(expectedSrt, extracted.StandardOutput);
        return new(sourceStreams.Count(stream => stream.CodecType == "video"), sourceStreams.Count(stream => stream.CodecType == "audio"),
            sourceStreams.Count(stream => stream.CodecType == "subtitle"), subtitle.Index, subtitleCodec, true);
    }

    internal static void ValidateSubtitles(string expectedSrt, string actualSrt)
    {
        var expected = SubtitleTextFormat.ParseSrt(expectedSrt);
        var actual = SubtitleTextFormat.ParseSrt(actualSrt);
        if (actual.Length != expected.Length)
        {
            throw new InvalidDataException("新增字幕条目数量校验失败；未提交输出。");
        }

        for (var index = 0; index < expected.Length; index++)
        {
            if (actual[index].Text != expected[index].Text || !Near(actual[index].Start, expected[index].Start, new(1, 1000)) ||
                !Near(actual[index].End, expected[index].End, new(1, 1000)))
            {
                throw new InvalidDataException("新增字幕文本或时间校验失败；目标容器可能不能保留重叠字幕，未提交输出。");
            }
        }
    }

    private static void ValidateCopiedStream(MediaStreamInfo original, MediaStreamInfo copied)
    {
        if (original.CodecType != copied.CodecType || original.CodecName != copied.CodecName ||
            original.Video is { } video && (copied.Video is not { } output || video.Width != output.Width ||
                video.Height != output.Height || video.PixelFormat != output.PixelFormat || video.Color != output.Color ||
                video.SampleAspectRatio != output.SampleAspectRatio ||
                !video.MasteringDisplays.SequenceEqual(output.MasteringDisplays) ||
                !video.ContentLightLevels.SequenceEqual(output.ContentLightLevels) ||
                !video.DisplayMatrices.SequenceEqual(output.DisplayMatrices)) ||
            original.Audio is { } audio && (copied.Audio is not { } outputAudio || audio.SampleRate != outputAudio.SampleRate ||
                audio.Channels != outputAudio.Channels || audio.ChannelLayout != outputAudio.ChannelLayout) ||
            original.Disposition.Any(pair => !copied.Disposition.TryGetValue(pair.Key, out var value) || value != pair.Value))
        {
            throw new InvalidDataException($"源流 {original.Index} 的编码、参数或播放偏好未原样保存；未提交输出。");
        }

        foreach (var name in new[] { "language", "title" })
        {
            var value = original.Tags.FirstOrDefault(pair => pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
            var outputValue = copied.Tags.FirstOrDefault(pair => pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
            if (value is not null && value != outputValue && !(name == "language" && value == "und" && outputValue is null))
            {
                throw new InvalidDataException($"源流 {original.Index} 的语言或名称未保存；未提交输出。");
            }
        }
    }

    private static async Task ValidatePacketStartAsync(string ffprobe, string sourcePath, MediaStreamInfo source,
        string outputPath, MediaStreamInfo output, CancellationToken cancellationToken)
    {
        var before = await ReadFirstPacketAsync(ffprobe, sourcePath, source.Index, source.Timing.TimeBase, cancellationToken).ConfigureAwait(false);
        var after = await ReadFirstPacketAsync(ffprobe, outputPath, output.Index, output.Timing.TimeBase, cancellationToken).ConfigureAwait(false);
        var tolerance = PacketTolerance(source, output);
        if (before is { } expected && (after is not { } actual || !Near(expected, actual, tolerance)))
        {
            throw new InvalidDataException($"源流 {source.Index} 的媒体时间发生偏移；未提交输出。");
        }
    }

    private static MediaTime PacketTolerance(MediaStreamInfo source, MediaStreamInfo output)
    {
        var tolerance = new MediaTime(1, 1000);
        if (source.Timing.TimeBase is { } sourceBase)
        {
            tolerance += new MediaTimestamp(1, sourceBase).ToMediaTime();
        }
        if (output.Timing.TimeBase is { } outputBase)
        {
            tolerance += new MediaTimestamp(1, outputBase).ToMediaTime();
        }
        return tolerance;
    }

    private static async Task ValidateAudioPacketsAsync(string ffprobe, string sourcePath, MediaStreamInfo source,
        string outputPath, MediaStreamInfo output, CancellationToken cancellationToken)
    {
        var before = await SubtitleMuxAudioPackets.ReadAsync(ffprobe, sourcePath, source.Index, source.Timing.TimeBase, cancellationToken).ConfigureAwait(false);
        var after = await SubtitleMuxAudioPackets.ReadAsync(ffprobe, outputPath, output.Index, output.Timing.TimeBase, cancellationToken).ConfigureAwait(false);
        if (before.PacketCount != after.PacketCount || before.InitialPadding != after.InitialPadding ||
            before.SkipSamples != after.SkipSamples || before.DiscardPadding != after.DiscardPadding ||
            before.End is { } expected && (after.End is not { } actual || !Near(expected, actual, PacketTolerance(source, output))))
        {
            throw new InvalidDataException($"源音轨 {source.Index} 的包数量、结束时间或首尾采样裁剪未保留；请选择兼容容器，未提交输出。");
        }
    }

    private static async Task<MediaTime?> ReadFirstPacketAsync(string ffprobe, string path, int streamIndex,
        MediaTimeBase? timeBase, CancellationToken cancellationToken)
    {
        var result = await MediaToolProcess.RunAsync(ffprobe,
            ["-v", "error", "-select_streams", streamIndex.ToString(CultureInfo.InvariantCulture), "-read_intervals", "%+#1",
                "-show_packets", "-show_entries", "packet=pts", "-of", "json", path],
            TimeSpan.FromSeconds(30), 65536, 65536, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new InvalidDataException($"无法验证源流时间：{result.StandardError}");
        }

        using var json = JsonDocument.Parse(result.StandardOutput);
        return timeBase is { } known && json.RootElement.GetProperty("packets").EnumerateArray().FirstOrDefault() is { ValueKind: JsonValueKind.Object } packet &&
            packet.TryGetProperty("pts", out var pts) ? new MediaTimestamp(pts.GetInt64(), known).ToMediaTime() : null;
    }

    internal static async Task<HashSet<int>> FindChapterCarriersAsync(string ffprobe, MediaProbeReport source,
        int chapterCount, CancellationToken cancellationToken)
    {
        var result = new HashSet<int>();
        if (chapterCount == 0 || source.Asset.FormatName?.Split(',').Contains("mov", StringComparer.Ordinal) != true)
        {
            return result;
        }

        foreach (var stream in source.Asset.Streams.Where(stream => stream.CodecType == "data" && stream.CodecName == "bin_data" &&
                     stream.Tags.TryGetValue("handler_name", out var handler) && handler == "SubtitleHandler"))
        {
            var details = await MediaToolProcess.RunAsync(ffprobe,
                ["-v", "error", "-select_streams", stream.Index.ToString(CultureInfo.InvariantCulture), "-read_intervals", "%+#1",
                    "-show_streams", "-show_packets", "-show_entries", "stream=codec_tag_string,nb_frames:packet=pts", "-of", "json", source.SourcePath],
                TimeSpan.FromSeconds(30), 65536, 65536, cancellationToken).ConfigureAwait(false);
            if (details.ExitCode != 0)
            {
                throw new InvalidDataException($"无法识别章节载体：{details.StandardError}");
            }

            using var json = JsonDocument.Parse(details.StandardOutput);
            var item = json.RootElement.GetProperty("streams")[0];
            if (item.TryGetProperty("codec_tag_string", out var tag) && tag.GetString() == "text" &&
                item.TryGetProperty("nb_frames", out var frames) &&
                int.TryParse(frames.GetString(), CultureInfo.InvariantCulture, out var frameCount) && frameCount == chapterCount &&
                json.RootElement.GetProperty("packets").GetArrayLength() == 0)
            {
                result.Add(stream.Index);
            }
        }

        return result;
    }

    internal static async Task<string[]> ReadChaptersAsync(string ffprobe, string path, CancellationToken cancellationToken)
    {
        var result = await MediaToolProcess.RunAsync(ffprobe, ["-v", "error", "-show_chapters", "-of", "json", path],
            TimeSpan.FromSeconds(30), 1024 * 1024, 65536, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new InvalidDataException($"无法验证章节：{result.StandardError}");
        }

        using var json = JsonDocument.Parse(result.StandardOutput);
        return json.RootElement.GetProperty("chapters").EnumerateArray().Select(chapter =>
            $"{chapter.GetProperty("start_time").GetString()}|{chapter.GetProperty("end_time").GetString()}|" +
            (chapter.TryGetProperty("tags", out var tags) ? tags.GetRawText() : string.Empty)).ToArray();
    }

    private static bool Near(MediaTime actual, MediaTime expected, MediaTime tolerance) =>
        actual - expected >= -tolerance && actual - expected <= tolerance;
}
