using System.Globalization;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Export.Muxing;
using AegiNext.Export.Worker;
using AegiNext.Media.Probing;
using AegiNext.Media.Processes;

namespace AegiNext.Export.Audio;

internal static class AudioExportProcessor
{
    internal static async Task<AudioExportReceipt> RunAsync(AudioExportRequest request,
        IProgress<AudioExportProgress>? progress, CancellationToken cancellationToken)
    {
        AudioExportRequestValidator.Validate(request);
        progress?.Report(new(MediaTime.Zero, null, "preflight"));
        var ffmpeg = ExportExecutable.Resolve("ffmpeg", request.FfmpegPath);
        await MediaToolchain.ValidateFfmpegAsync(ffmpeg, cancellationToken).ConfigureAwait(false);
        var ffprobe = Path.Combine(Path.GetDirectoryName(ffmpeg)!, OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
        var probe = new FfprobeMediaProbe(new(ffprobe));
        var binding = request.Project.Media!;
        var sourcePath = ProjectAssetLocation.Resolve(request.Project.Assets.Single(asset => asset.Id == binding.AssetId), request.ProjectDirectory);
        var source = await probe.ProbeAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        var selected = binding.AudioStreamIndex is { } index
            ? source.Asset.Streams.SingleOrDefault(stream => stream.Index == index)
            : source.Asset.Streams.FirstOrDefault(stream => stream.CodecType == "audio");
        if (selected is not { CodecType: "audio", Audio: { SampleRate: > 0, Channels: > 0 } audio } ||
            string.IsNullOrWhiteSpace(selected.CodecName))
        {
            throw new InvalidDataException("源媒体没有可导出的选定音轨。");
        }

        if (request.Format == AudioFileFormat.Mp3 && audio.Channels > 2)
        {
            throw new NotSupportedException("MP3 仅支持单声道或立体声；请选择能保留当前声道的音频格式。");
        }

        var arguments = new List<string>
        {
            "-nostdin", "-v", "error", "-xerror", "-nostats", "-progress", "pipe:1", "-i", sourcePath,
            "-map", $"0:{selected.Index.ToString(CultureInfo.InvariantCulture)}", "-vn", "-sn", "-dn",
            "-c:a", AudioExportCapabilities.GetEncoder(request.Format), "-map_metadata", "0", "-map_chapters", "-1"
        };
        if (AudioExportCapabilities.UsesBitrate(request.Format))
        {
            arguments.AddRange(["-b:a", request.Bitrate.ToString(CultureInfo.InvariantCulture)]);
        }

        if (request.Format != AudioFileFormat.Copy)
        {
            arguments.AddRange(["-af", "asetpts=PTS-STARTPTS"]);
        }

        if (request.Format == AudioFileFormat.Wav)
        {
            arguments.AddRange(["-rf64", "auto"]);
        }

        arguments.AddRange(["-n", request.OutputPath]);
        var duration = selected.Timing.ReportedDuration ?? source.Asset.ReportedDuration;
        progress?.Report(new(MediaTime.Zero, duration is not null ? 0 : null, "encoding"));
        await ExportMediaProcess.RunAsync(ffmpeg, arguments, position =>
        {
            var fraction = duration is { } end && end > MediaTime.Zero
                ? Math.Clamp((double)position.Numerator / position.Denominator / ((double)end.Numerator / end.Denominator), 0, 1)
                : (double?)null;
            progress?.Report(new(position, fraction, "encoding"));
        }, cancellationToken, "无法将选定音轨保存为目标音频格式；未提交输出。").ConfigureAwait(false);

        progress?.Report(new(MediaTime.Zero, null, "verifying"));
        var output = await probe.ProbeAsync(request.OutputPath, cancellationToken).ConfigureAwait(false);
        var expectedCodec = request.Format == AudioFileFormat.Copy ? selected.CodecName : AudioExportCapabilities.GetCodec(request.Format);
        if (output.Asset.Streams.Length != 1 || output.Asset.Streams[0] is not
            { CodecType: "audio", Audio: { SampleRate: > 0, Channels: > 0 } outputAudio } exported ||
            exported.CodecName != expectedCodec || outputAudio.Channels != audio.Channels ||
            request.Format is AudioFileFormat.Copy or AudioFileFormat.Wav or AudioFileFormat.Flac && outputAudio.SampleRate != audio.SampleRate)
        {
            throw new InvalidDataException("输出文件未保留选定音轨的编码、采样率或声道，或包含额外媒体流；未提交输出。");
        }

        var outputHash = await ReadDecodedHashAsync(ffmpeg, request.OutputPath, exported.Index, cancellationToken).ConfigureAwait(false);
        if (request.Format == AudioFileFormat.Copy && outputHash !=
            await ReadDecodedHashAsync(ffmpeg, sourcePath, selected.Index, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidDataException("目标容器未原样保留音频内容或首尾采样裁剪；请选择 WAV、FLAC 或其他编码格式，未提交输出。");
        }

        return new(selected.Index, exported.CodecName!, outputAudio.SampleRate!.Value, outputAudio.Channels!.Value, true);
    }

    private static async Task<string> ReadDecodedHashAsync(string ffmpeg, string path, int streamIndex, CancellationToken cancellationToken)
    {
        var output = await MediaToolProcess.RunAsync(ffmpeg,
            ["-nostdin", "-v", "error", "-xerror", "-i", path, "-map", $"0:{streamIndex.ToString(CultureInfo.InvariantCulture)}",
                "-vn", "-sn", "-dn", "-c:a", "pcm_s32le", "-f", "hash", "-hash", "sha256", "pipe:1"],
            TimeSpan.FromHours(24), 4096, 65536, cancellationToken).ConfigureAwait(false);
        var hash = output.StandardOutput.Trim();
        if (output.ExitCode != 0 || !hash.StartsWith("SHA256=", StringComparison.Ordinal) ||
            hash == "SHA256=e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")
        {
            throw new InvalidDataException($"无法验证完整音频内容；未提交输出。{Environment.NewLine}{output.StandardError}");
        }

        return hash;
    }
}
