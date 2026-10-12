using System.Globalization;
using AegiNext.Core.Media;
using AegiNext.Core.Projects;
using AegiNext.Media.Probing;
using AegiNext.Media.Processes;

namespace AegiNext.Export;

internal static class ExportMuxer
{
    internal static async Task<ExportAudioPlan> PreflightAsync(VideoExportRequest request, CancellationToken cancellationToken)
    {
        var ffmpeg = ExportExecutable.Resolve("ffmpeg", request.FfmpegPath);
        await MediaToolchain.ValidateFfmpegAsync(ffmpeg, cancellationToken).ConfigureAwait(false);
        var probe = CreateProbe(ffmpeg);
        var binding = request.Project.Media!;
        var sourcePath = ProjectAssetLocation.Resolve(request.Project.Assets.Single(asset => asset.Id == binding.AssetId), request.ProjectDirectory);
        var source = await probe.ProbeAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        var video = source.Asset.Streams.Single(stream => stream.Index == binding.VideoStreamIndex).Video
            ?? throw new InvalidDataException("The selected stream is not a video stream.");
        if (request.Codec == VideoCodec.Vp8 && (video.Color.IsPq || video.Color.IsHlg))
        {
            throw new NotSupportedException("VP8 only supports SDR; use VP9, AV1, or HEVC for HDR.");
        }

        int? audioIndex = null;
        if (request.AudioMode != AudioExportMode.None)
        {
            audioIndex = binding.AudioStreamIndex ?? source.Asset.Streams.FirstOrDefault(stream => stream.CodecType == "audio")?.Index;
        }

        var selected = audioIndex is { } index ? source.Asset.Streams.Single(stream => stream.Index == index) : null;
        if (selected is not null && selected.CodecType != "audio")
        {
            throw new InvalidDataException("The selected audio track is not an audio stream.");
        }

        ValidateAudioCompatibility(Path.GetExtension(request.OutputPath).ToLowerInvariant(), request.AudioMode, selected?.CodecName);
        if (selected is not null && request.AudioMode == AudioExportMode.Copy)
        {
            var trialPath = Path.Combine(Path.GetDirectoryName(request.OutputPath)!, "audio-compatibility" + Path.GetExtension(request.OutputPath));
            try
            {
                await RunToolAsync(ffmpeg,
                    ["-nostdin", "-v", "error", "-copyts", "-i", sourcePath, "-map", $"0:{selected.Index.ToString(CultureInfo.InvariantCulture)}",
                        "-vn", "-c:a", "copy", "-frames:a", "1", "-map_metadata", "-1", "-map_chapters", "-1", "-avoid_negative_ts", "disabled", "-n", trialPath],
                    TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException error)
            {
                throw new NotSupportedException("The target container cannot copy the selected audio track; choose a compatible container or explicitly select AAC or Opus.", error);
            }
            finally
            {
                File.Delete(trialPath);
            }
        }

        return new(ffmpeg, sourcePath, selected?.Index, selected?.CodecName);
    }

    internal static void ValidateAudioCompatibility(string extension, AudioExportMode mode, string? codec)
    {
        if (extension == ".webm" && (mode == AudioExportMode.Aac ||
            mode == AudioExportMode.Copy && codec is not null and not "opus" and not "vorbis"))
        {
            throw new NotSupportedException("WebM audio only supports Opus or Vorbis; select Opus or no audio, or use MKV to copy the original audio track.");
        }
    }

    internal static async Task RunAsync(VideoExportRequest request, NativeVideoExportSession session,
        ExportAudioPlan audio, CancellationToken cancellationToken)
    {
        string? audioPath = null;
        var audioIndex = -1;
        if (audio.StreamIndex is { } selected)
        {
            audioPath = audio.SourcePath;
            audioIndex = selected;
            if (request.AudioMode != AudioExportMode.Copy)
            {
                var extension = Path.GetExtension(request.OutputPath).Equals(".mp4", StringComparison.OrdinalIgnoreCase)
                    ? ".mp4" : request.AudioMode == AudioExportMode.Opus ? ".mka" : ".m4a";
                audioPath = Path.Combine(Path.GetDirectoryName(request.OutputPath)!, "audio" + extension);
                await RunToolAsync(audio.FfmpegPath,
                    ["-nostdin", "-v", "error", "-copyts", "-i", audio.SourcePath, "-map", $"0:{selected.ToString(CultureInfo.InvariantCulture)}",
                        "-vn", "-c:a", request.AudioMode == AudioExportMode.Opus ? "libopus" : "aac", "-b:a", request.AudioBitrate.ToString(CultureInfo.InvariantCulture),
                        "-map_metadata", "0", "-map_chapters", "-1", "-avoid_negative_ts", "disabled", "-n", audioPath],
                    TimeSpan.FromHours(24), cancellationToken).ConfigureAwait(false);
                audioIndex = 0;
            }
        }

        session.Mux(request.OutputPath, audioPath, audioIndex, cancellationToken);
        var encodedResult = session.ReadResult(cancellationToken);
        var outputColor = encodedResult.OutputColor;
        var expected = outputColor.ToMetadata();
        var result = await CreateProbe(audio.FfmpegPath).ProbeAsync(request.OutputPath, cancellationToken).ConfigureAwait(false);
        var stream = result.Asset.Streams.Single(stream => stream.CodecType == "video");
        var video = stream.Video!;
        var expectedCodec = outputColor.Codec switch
        {
            VideoCodec.H264 => "h264", VideoCodec.Hevc => "hevc", VideoCodec.Vp8 => "vp8",
            VideoCodec.Vp9 => "vp9", VideoCodec.Av1 => "av1", _ => throw new InvalidDataException("The actual output codec is missing.")
        };
        if (stream.CodecName != expectedCodec || video.Width != request.Project.Width || video.Height != request.Project.Height ||
            video.Color.Transfer != expected.Transfer || video.Color.Primaries != expected.Primaries ||
            video.Color.Matrix != expected.Matrix || video.Color.Range != expected.Range || video.PixelFormat != outputColor.PixelFormat ||
            (expected.IsPq || expected.IsHlg) && outputColor.BitDepth < 10)
        {
            throw new InvalidDataException("Output video validation failed for the codec, dimensions, color tags, or HDR bit depth; the output was not committed.");
        }

        var outputAudio = result.Asset.Streams.Where(item => item.CodecType == "audio").ToArray();
        if (audio.StreamIndex is not null)
        {
            var expectedAudioCodec = request.AudioMode switch
            {
                AudioExportMode.Copy => audio.CodecName, AudioExportMode.Aac => "aac", AudioExportMode.Opus => "opus", _ => null
            };
            if (outputAudio.Length != 1 || outputAudio[0].CodecName != expectedAudioCodec)
            {
                throw new InvalidDataException("The output video did not preserve the selected audio track or codec; the output was not committed.");
            }
        }
        else if (outputAudio.Length != 0)
        {
            throw new InvalidDataException("The output video contains an unexpected audio track despite audio being disabled; the output was not committed.");
        }
    }

    private static FfprobeMediaProbe CreateProbe(string ffmpeg) => new(new(Path.Combine(Path.GetDirectoryName(ffmpeg)!,
        OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe")));

    private static async Task RunToolAsync(string ffmpeg, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var output = await MediaToolProcess.RunAsync(ffmpeg, arguments, timeout, 65536, 65536, cancellationToken).ConfigureAwait(false);
        if (output.ExitCode != 0)
        {
            throw new InvalidOperationException($"Audio processing failed: {output.StandardError}");
        }
    }
}
