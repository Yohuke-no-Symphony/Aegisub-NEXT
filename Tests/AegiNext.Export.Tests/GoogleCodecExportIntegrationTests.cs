using System.Globalization;
using System.Text.Json;
using AegiNext.Core.Timing;
using AegiNext.Media.Audio;
using AegiNext.Media.Decoding;

namespace AegiNext.Export.Tests;

/// <summary>Google 视频编码经 worker、字幕合成和最终封装的真实输出合同。</summary>
[Collection(nameof(NativeExportTestGroup))]
public sealed class GoogleCodecExportIntegrationTests
{
    /// <summary>有效 SDR 组合必须烧录字幕，保留完整时间轴并产生指定视频和音频编码。</summary>
    [ExportTheory]
    [InlineData(VideoCodec.Vp8, ".webm", AudioExportMode.Opus)]
    [InlineData(VideoCodec.Vp9, ".webm", AudioExportMode.Opus)]
    [InlineData(VideoCodec.Av1, ".webm", AudioExportMode.Opus)]
    [InlineData(VideoCodec.Vp8, ".mkv", AudioExportMode.Copy)]
    [InlineData(VideoCodec.Vp9, ".mkv", AudioExportMode.Copy)]
    [InlineData(VideoCodec.Av1, ".mkv", AudioExportMode.Copy)]
    [InlineData(VideoCodec.Vp9, ".mp4", AudioExportMode.Opus)]
    [InlineData(VideoCodec.Av1, ".mp4", AudioExportMode.Opus)]
    [InlineData(VideoCodec.Vp9, ".mp4", AudioExportMode.Aac)]
    [InlineData(VideoCodec.Av1, ".mp4", AudioExportMode.Aac)]
    public async Task SoftwareWorkerExportsSubtitleAndSelectedAudio(VideoCodec codec, string extension, AudioExportMode audioMode)
    {
        using var fixture = await GoogleCodecExportFixture.CreateAsync();
        var request = fixture.CreateRequest(codec, extension, audioMode);

        var result = await new VideoExporter().ExportAsync(request);

        await AssertVideoAsync(fixture, result, codec, extension, "bt709", 8);
        Assert.Equal(VideoExportCapabilities.Get(codec).SoftwareEncoder, result.Encoder);
        Assert.Equal(request.RateControlMode, result.RateControl!.Mode);
        Assert.Equal(request.Quality, result.RateControl.Quality);
        Assert.Equal(codec == VideoCodec.Vp8 ? request.VideoBitrate : 0, result.RateControl.VideoBitrate);
        Assert.Equal(VideoRateControlVerification.ConfiguredAndInitialized, result.RateControl.Verification);
        var audio = await GoogleCodecExportFixture.ProbeAudioAsync(result.OutputPath);
        var stream = Assert.Single(audio.GetProperty("streams").EnumerateArray());
        Assert.Equal(audioMode == AudioExportMode.Opus ? "opus" : "aac", stream.GetProperty("codec_name").GetString());
        Assert.NotEmpty(audio.GetProperty("packets").EnumerateArray());
        if (audioMode == AudioExportMode.Copy)
        {
            var source = await GoogleCodecExportFixture.ProbeAudioAsync(fixture.MediaPath);
            Assert.Equal(PacketHashes(source), PacketHashes(audio));
            AssertPacketBoundaries(source, audio);
        }
        fixture.AssertCleanOutput(true);
    }

    /// <summary>HDR 的十位像素、颜色标签及完整静态元数据必须可从最终成片独立读取。</summary>
    [ExportTheory]
    [InlineData(VideoCodec.Vp9, ".mkv", "smpte2084")]
    [InlineData(VideoCodec.Vp9, ".webm", "smpte2084")]
    [InlineData(VideoCodec.Vp9, ".mp4", "smpte2084")]
    [InlineData(VideoCodec.Av1, ".mkv", "smpte2084")]
    [InlineData(VideoCodec.Av1, ".webm", "smpte2084")]
    [InlineData(VideoCodec.Av1, ".mp4", "smpte2084")]
    [InlineData(VideoCodec.Vp9, ".mkv", "arib-std-b67")]
    [InlineData(VideoCodec.Vp9, ".webm", "arib-std-b67")]
    [InlineData(VideoCodec.Vp9, ".mp4", "arib-std-b67")]
    [InlineData(VideoCodec.Av1, ".mkv", "arib-std-b67")]
    [InlineData(VideoCodec.Av1, ".webm", "arib-std-b67")]
    [InlineData(VideoCodec.Av1, ".mp4", "arib-std-b67")]
    public async Task HdrWorkerPreservesTenBitColorAndCompleteMasteringMetadata(VideoCodec codec, string extension, string transfer)
    {
        using var fixture = await GoogleCodecExportFixture.CreateAsync(transfer);
        var source = await GoogleCodecExportFixture.ProbeVideoAsync(fixture.MediaPath);
        AssertStaticHdr(source);
        var request = fixture.CreateRequest(codec, extension, AudioExportMode.None);

        var result = await new VideoExporter().ExportAsync(request);

        var output = await AssertVideoAsync(fixture, result, codec, extension, transfer, 10);
        AssertStaticHdr(output);
        Assert.NotNull(result.OutputColor!.MasteringDisplay);
        Assert.NotNull(result.OutputColor.ContentLight);
        var audio = await GoogleCodecExportFixture.ProbeAudioAsync(result.OutputPath);
        Assert.Empty(audio.GetProperty("streams").EnumerateArray());
        fixture.AssertCleanOutput(true);
    }

    /// <summary>不支持的 HDR 与音轨组合在编码首帧前拒绝，临时文件与成片均不得残留。</summary>
    [ExportTheory]
    [InlineData(VideoCodec.Vp8, ".mkv", "smpte2084", AudioExportMode.None, "VP8")]
    [InlineData(VideoCodec.Vp8, ".mkv", "arib-std-b67", AudioExportMode.None, "VP8")]
    [InlineData(VideoCodec.Vp9, ".webm", null, AudioExportMode.Copy, "WebM")]
    [InlineData(VideoCodec.Av1, ".webm", null, AudioExportMode.Copy, "WebM")]
    public async Task IncompatibleInputIsRejectedBeforeEncodingAndNeverPublishes(VideoCodec codec, string extension,
        string? transfer, AudioExportMode audioMode, string reason)
    {
        using var fixture = await GoogleCodecExportFixture.CreateAsync(transfer);
        var request = fixture.CreateRequest(codec, extension, audioMode);
        var progress = new ExportProgressCapture();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new VideoExporter().ExportAsync(request, progress));

        Assert.Contains(reason, error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(progress.Values, value => value.Stage == "encoding");
        fixture.AssertCleanOutput(false);
    }

    /// <summary>独立音频准备阶段的包载荷、首尾 PTS、跳过样本与最终可解码采样长度均保持不变。</summary>
    [ExportTheory]
    [InlineData(AudioExportMode.Aac, ".mp4")]
    [InlineData(AudioExportMode.Opus, ".mkv")]
    public async Task PreparedAudioRetainsPacketTimingAndEncoderDelay(AudioExportMode audioMode, string extension)
    {
        using var fixture = await GoogleCodecExportFixture.CreateAsync();
        var prepared = await fixture.PrepareAudioAsync(audioMode);
        var before = await GoogleCodecExportFixture.ProbeAudioAsync(prepared);
        var request = fixture.CreateRequest(VideoCodec.Vp9, extension, audioMode);

        var result = await new VideoExporter().ExportAsync(request);

        var after = await GoogleCodecExportFixture.ProbeAudioAsync(result.OutputPath);
        Assert.Equal(PacketHashes(before), PacketHashes(after));
        AssertPacketBoundaries(before, after);
        var packets = before.GetProperty("packets").EnumerateArray().ToArray();
        Assert.True(SkipSamples(packets[0], "skip_samples") + SkipSamples(packets[^1], "discard_padding") > 0,
            "The prepared fixture must exercise encoder delay or end padding.");
        var expected = DecodeAudioExtent(prepared, 0);
        var actual = DecodeAudioExtent(result.OutputPath, 1);
        Assert.Equal(expected.Frames, actual.Frames);
        Assert.InRange(Math.Abs(expected.Start - actual.Start), 0, 0.002);
        fixture.AssertCleanOutput(true);
    }

    /// <summary>MKV 的毫秒时间基与 MP4 的采样时间基可用不同字段表达同一组 AAC 首尾填充。</summary>
    [ExportTheory]
    [InlineData(VideoCodec.H264)]
    public async Task Mp4AudioCopyAcceptsEquivalentAacPaddingRepresentation(VideoCodec codec)
    {
        using var fixture = await GoogleCodecExportFixture.CreateAsync();
        var request = fixture.CreateRequest(codec, ".mp4", AudioExportMode.Copy);

        var result = await new VideoExporter().ExportAsync(request);

        var before = await GoogleCodecExportFixture.ProbeAudioAsync(fixture.MediaPath);
        var after = await GoogleCodecExportFixture.ProbeAudioAsync(result.OutputPath);
        Assert.Equal(PacketHashes(before), PacketHashes(after));
        AssertPacketBoundaries(before, after, exactPadding: false);
        AssertEquivalentAacPadding(before, after);
        fixture.AssertCleanOutput(true);
    }

    private static async Task<JsonElement> AssertVideoAsync(GoogleCodecExportFixture fixture, VideoExportResult result,
        VideoCodec codec, string extension, string transfer, int depth)
    {
        Assert.Equal((ulong)GoogleCodecExportFixture.FRAME_COUNT, result.Frames);
        Assert.Equal(codec, result.OutputColor!.Codec);
        Assert.Equal(depth, result.OutputColor.BitDepth);
        Assert.Equal(3U, result.OutputColor.OutputVerification);
        var output = await GoogleCodecExportFixture.ProbeVideoAsync(result.OutputPath);
        var stream = Assert.Single(output.GetProperty("streams").EnumerateArray());
        Assert.Equal(codec.ToString().ToLowerInvariant(), stream.GetProperty("codec_name").GetString());
        Assert.Equal(GoogleCodecExportFixture.WIDTH, stream.GetProperty("width").GetInt32());
        Assert.Equal(GoogleCodecExportFixture.HEIGHT, stream.GetProperty("height").GetInt32());
        Assert.Equal(depth == 10 ? "yuv420p10le" : "yuv420p", stream.GetProperty("pix_fmt").GetString());
        Assert.Equal(transfer, stream.GetProperty("color_transfer").GetString());
        Assert.Equal(depth == 10 ? "bt2020" : "bt709", stream.GetProperty("color_primaries").GetString());
        Assert.Equal(depth == 10 ? "bt2020nc" : "bt709", stream.GetProperty("color_space").GetString());
        Assert.Equal("tv", stream.GetProperty("color_range").GetString());
        Assert.Equal(GoogleCodecExportFixture.FRAME_COUNT, output.GetProperty("frames").GetArrayLength());
        var format = output.GetProperty("format");
        Assert.Contains(extension == ".mp4" ? "mp4" : "matroska", format.GetProperty("format_name").GetString()!);
        Assert.InRange(Number(format.GetProperty("duration")), 1.95, 2.1);
        AssertDecodedSubtitleAndTimeline(fixture.MediaPath, result.OutputPath, depth);
        return output;
    }

    private static void AssertDecodedSubtitleAndTimeline(string sourcePath, string outputPath, int depth)
    {
        using var source = FfmpegVideoDecoder.Open(sourcePath, 0, options: new() { Mode = VideoDecodeMode.Software });
        using var output = FfmpegVideoDecoder.Open(outputPath, 0, options: new() { Mode = VideoDecodeMode.Software });
        for (var index = 0; index < GoogleCodecExportFixture.FRAME_COUNT; index++)
        {
            using var original = source.ReadFrame();
            using var rendered = output.ReadFrame();
            Assert.NotNull(original);
            Assert.NotNull(rendered);
            Assert.Equal(depth, rendered.Info.ComponentDepths[0]);
            var before = original.Info.PresentationTimestamp!.ToMediaTime().ToTimeSpan(MediaTimeRounding.FLOOR).TotalSeconds;
            var after = rendered.Info.PresentationTimestamp!.ToMediaTime().ToTimeSpan(MediaTimeRounding.FLOOR).TotalSeconds;
            Assert.InRange(Math.Abs(before - after), 0, 0.002);
            if (index != GoogleCodecExportFixture.FRAME_COUNT / 2)
            {
                continue;
            }
            var luma = rendered.CopyPlane(0);
            var stride = rendered.GetPlaneInfo(0).RowBytes;
            var brightPixels = 0;
            for (var y = 32; y < 96; y++)
            {
                for (var x = 16; x < 176; x++)
                {
                    var offset = y * stride + x * (depth == 10 ? 2 : 1);
                    var value = depth == 10 ? BitConverter.ToUInt16(luma, offset) : luma[offset];
                    if (value > (depth == 10 ? 200 : 80))
                    {
                        brightPixels++;
                    }
                }
            }
            Assert.True(brightPixels > 100, $"Subtitle must be visible over the black source; found {brightPixels} bright pixels.");
        }
        Assert.Null(source.ReadFrame());
        Assert.Null(output.ReadFrame());
    }

    private static void AssertStaticHdr(JsonElement probe)
    {
        var sideData = new List<JsonElement>();
        foreach (var owner in probe.GetProperty("streams").EnumerateArray().Concat(probe.GetProperty("frames").EnumerateArray()))
        {
            if (owner.TryGetProperty("side_data_list", out var values))
            {
                sideData.AddRange(values.EnumerateArray());
            }
        }
        var mastering = sideData.Where(value => value.GetProperty("side_data_type").GetString()!
            .Contains("Mastering display", StringComparison.OrdinalIgnoreCase)).ToArray();
        var light = sideData.Where(value => value.GetProperty("side_data_type").GetString()!
            .Contains("Content light", StringComparison.OrdinalIgnoreCase)).ToArray();
        Assert.NotEmpty(mastering);
        Assert.NotEmpty(light);
        string[] names = ["red_x", "red_y", "green_x", "green_y", "blue_x", "blue_y", "white_point_x", "white_point_y", "min_luminance", "max_luminance"];
        double[] expected = [0.68, 0.32, 0.265, 0.69, 0.15, 0.06, 0.3127, 0.329, 0.005, 1000];
        foreach (var value in mastering)
        {
            for (var index = 0; index < names.Length; index++)
            {
                Assert.InRange(Math.Abs(Number(value.GetProperty(names[index])) - expected[index]), 0, 0.00002);
            }
        }
        foreach (var value in light)
        {
            Assert.Equal(1000, value.GetProperty("max_content").GetInt32());
            Assert.Equal(400, value.GetProperty("max_average").GetInt32());
        }
    }

    private static string[] PacketHashes(JsonElement probe) => probe.GetProperty("packets").EnumerateArray()
        .Select(packet => packet.GetProperty("data_hash").GetString()!).ToArray();

    private static void AssertPacketBoundaries(JsonElement before, JsonElement after, bool exactPadding = true)
    {
        var expected = before.GetProperty("packets").EnumerateArray().ToArray();
        var actual = after.GetProperty("packets").EnumerateArray().ToArray();
        Assert.Equal(expected.Length, actual.Length);
        Assert.NotEmpty(expected);
        foreach (var index in new[] { 0, expected.Length - 1 })
        {
            Assert.InRange(Math.Abs(Number(expected[index].GetProperty("pts_time")) - Number(actual[index].GetProperty("pts_time"))), 0, 0.002);
            if (exactPadding)
            {
                Assert.Equal(SkipSamples(expected[index], "skip_samples"), SkipSamples(actual[index], "skip_samples"));
                Assert.Equal(SkipSamples(expected[index], "discard_padding"), SkipSamples(actual[index], "discard_padding"));
            }
        }
    }

    private static void AssertEquivalentAacPadding(JsonElement before, JsonElement after)
    {
        var beforeStream = Assert.Single(before.GetProperty("streams").EnumerateArray());
        var afterStream = Assert.Single(after.GetProperty("streams").EnumerateArray());
        Assert.Equal("aac", beforeStream.GetProperty("codec_name").GetString());
        Assert.Equal("aac", afterStream.GetProperty("codec_name").GetString());
        var sampleRate = int.Parse(beforeStream.GetProperty("sample_rate").GetString()!, CultureInfo.InvariantCulture);
        Assert.Equal(sampleRate.ToString(CultureInfo.InvariantCulture), afterStream.GetProperty("sample_rate").GetString());
        var beforePackets = before.GetProperty("packets").EnumerateArray().ToArray();
        var afterPackets = after.GetProperty("packets").EnumerateArray().ToArray();
        var beforeLeading = Math.Max(StreamPadding(beforeStream, "initial_padding"), SkipSamples(beforePackets[0], "skip_samples"));
        var afterLeading = Math.Max(StreamPadding(afterStream, "initial_padding"), SkipSamples(afterPackets[0], "skip_samples"));
        var beforeStart = Number(beforePackets[0].GetProperty("pts_time")) + (double)beforeLeading / sampleRate;
        var afterStart = Number(afterPackets[0].GetProperty("pts_time")) + (double)afterLeading / sampleRate;
        var tolerance = Math.Max(Number(beforeStream.GetProperty("time_base")), Number(afterStream.GetProperty("time_base")));
        Assert.InRange(Math.Abs(beforeStart - afterStart), 0, tolerance);
        Assert.InRange(Math.Abs(EffectiveAacTrailingPadding(beforeStream, beforePackets[^1], sampleRate) -
            EffectiveAacTrailingPadding(afterStream, afterPackets[^1], sampleRate)), 0, (long)Math.Ceiling(tolerance * sampleRate));
    }

    private static long EffectiveAacTrailingPadding(JsonElement stream, JsonElement packet, int sampleRate)
    {
        var duration = (long)Math.Round(Number(packet.GetProperty("duration_time")) * sampleRate);
        var implicitPadding = Math.Max(0, 1024 - duration);
        return Math.Max(StreamPadding(stream, "trailing_padding"),
            Math.Max(implicitPadding, SkipSamples(packet, "discard_padding")));
    }

    private static long StreamPadding(JsonElement stream, string name) =>
        stream.TryGetProperty(name, out var value) ? value.GetInt64() : 0;

    private static long SkipSamples(JsonElement packet, string name)
    {
        if (!packet.TryGetProperty("side_data_list", out var sideData))
        {
            return 0;
        }
        return sideData.EnumerateArray().Where(value => value.TryGetProperty(name, out _))
            .Sum(value => value.GetProperty(name).GetInt64());
    }

    private static (double Start, long Frames) DecodeAudioExtent(string path, int index)
    {
        using var decoder = FfmpegAudioDecoder.Open(path, index, new(48000, 1));
        var frames = 0L;
        double? start = null;
        while (decoder.Read() is { } block)
        {
            start ??= block.Start.ToTimeSpan(MediaTimeRounding.FLOOR).TotalSeconds;
            frames += block.FrameCount;
        }
        Assert.NotNull(start);
        Assert.True(frames > 0);
        return (start.Value, frames);
    }

    private static double Number(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number)
        {
            return value.GetDouble();
        }
        var parts = value.GetString()!.Split('/');
        var numerator = double.Parse(parts[0], CultureInfo.InvariantCulture);
        return parts.Length == 1 ? numerator : numerator / double.Parse(parts[1], CultureInfo.InvariantCulture);
    }
}
