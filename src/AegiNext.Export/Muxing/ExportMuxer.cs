using System.Diagnostics;
using System.Globalization;
using AegiNext.Core.Projects;
using AegiNext.Media.Probing;

namespace AegiNext.Export;

internal static class ExportMuxer
{
    internal static async Task RunAsync(VideoExportRequest request, string videoPath, VideoExportColor outputColor,
        CancellationToken cancellationToken)
    {
        var ffmpeg = ExportExecutable.Resolve("ffmpeg", request.FfmpegPath);
        var probePath = Path.Combine(Path.GetDirectoryName(ffmpeg)!, OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
        var probe = new FfprobeMediaProbe(new(probePath));
        var binding = request.Project.Media!;
        var sourcePath = ProjectAssetLocation.Resolve(request.Project.Assets.Single(asset => asset.Id == binding.AssetId), request.ProjectDirectory);
        var source = await probe.ProbeAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        _ = source.Asset.Streams.Single(stream => stream.Index == binding.VideoStreamIndex).Video
            ?? throw new InvalidDataException("选定流不是视频。");
        var expectedColor = outputColor.ToMetadata();
        int? audioIndex = null;
        if (request.AudioMode != AudioExportMode.None)
        {
            audioIndex = binding.AudioStreamIndex ?? source.Asset.Streams.FirstOrDefault(stream => stream.CodecType == "audio")?.Index;
            if (audioIndex is { } index && source.Asset.Streams.Single(stream => stream.Index == index).CodecType != "audio")
            {
                throw new InvalidDataException("选定音轨不是音频流。");
            }
        }

        await MediaToolchain.ValidateFfmpegAsync(ffmpeg, cancellationToken).ConfigureAwait(false);

        var start = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true };
        foreach (var argument in new[] { "-nostdin", "-v", "error", "-copyts", "-i", videoPath, "-i", sourcePath, "-map", "0:v:0", "-c:v", "copy" })
        {
            start.ArgumentList.Add(argument);
        }

        if (audioIndex is { } selectedAudio)
        {
            start.ArgumentList.Add("-map");
            start.ArgumentList.Add("1:" + selectedAudio.ToString(CultureInfo.InvariantCulture));
            start.ArgumentList.Add("-c:a");
            start.ArgumentList.Add(request.AudioMode == AudioExportMode.Copy ? "copy" : "aac");
            if (request.AudioMode == AudioExportMode.Aac)
            {
                start.ArgumentList.Add("-b:a");
                start.ArgumentList.Add(request.AudioBitrate.ToString(CultureInfo.InvariantCulture));
            }
        }
        else
        {
            start.ArgumentList.Add("-an");
        }

        foreach (var argument in new[] { "-map_metadata", "1", "-map_chapters", "1", "-avoid_negative_ts", "disabled", "-n", request.OutputPath })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = start };
        if (!process.Start())
        {
            throw new InvalidOperationException("无法启动音轨复用进程。");
        }

        using var cancelled = cancellationToken.Register(() => VideoExporter.Kill(process));
        var errors = VideoExporter.ReadErrorsAsync(process.StandardError);
        var output = VideoExporter.ReadErrorsAsync(process.StandardOutput);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var diagnostics = await errors.ConfigureAwait(false);
            await output.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"音轨复用失败：{diagnostics}");
            }
        }
        finally
        {
            VideoExporter.Kill(process);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await errors.ConfigureAwait(false);
            await output.ConfigureAwait(false);
        }

        var result = await probe.ProbeAsync(request.OutputPath, cancellationToken).ConfigureAwait(false);
        var encoded = result.Asset.Streams.First(stream => stream.CodecType == "video").Video!;
        if (encoded.Width != request.Project.Width || encoded.Height != request.Project.Height ||
            encoded.Color.Transfer != expectedColor.Transfer || encoded.Color.Primaries != expectedColor.Primaries ||
            encoded.Color.Matrix != expectedColor.Matrix || encoded.Color.Range != expectedColor.Range ||
            (expectedColor.IsPq || expectedColor.IsHlg) && encoded.PixelFormat != "yuv420p10le")
        {
            throw new InvalidDataException("成片尺寸、色彩标签或 HDR 位深校验失败，未提交输出。");
        }

        if (audioIndex is not null && !result.Asset.Streams.Any(stream => stream.CodecType == "audio"))
        {
            throw new InvalidDataException("成片丢失选定音轨，未提交输出。");
        }
    }
}
