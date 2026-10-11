using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Media.Decoding;

namespace AegiNext.Export;

/// <summary>启动邻接的独立导出 worker，管理取消、进度和不覆盖旧文件的原子提交。</summary>
public sealed class VideoExporter
{
    /// <summary>压制当前工程；失败或取消只删除本次临时文件，已有成片保持原样。</summary>
    public Task<VideoExportResult> ExportAsync(VideoExportRequest request, IProgress<VideoExportProgress>? progress = null,
        CancellationToken cancellationToken = default) => ExportAsync(request, progress, null, cancellationToken);

    /// <summary>压制冻结快照，在原子发布成片前执行应用提供的提交边界。</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Exporter is a service instance used by the application boundary.")]
    public async Task<VideoExportResult> ExportAsync(VideoExportRequest request, IProgress<VideoExportProgress>? progress,
        Action? beforeCommit, CancellationToken cancellationToken)
    {
        Validate(request);
        cancellationToken.ThrowIfCancellationRequested();
        var output = Path.GetFullPath(request.OutputPath);
        if (File.Exists(output))
        {
            throw new IOException("输出文件已存在，请选择新文件名。");
        }

        var directory = Path.GetDirectoryName(output)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".aeginext-export-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporary);
        try
        {
            var ffmpeg = ExportExecutable.Resolve("ffmpeg", request.FfmpegPath ?? Environment.GetEnvironmentVariable("AEGINEXT_FFMPEG_PATH"));
            var worker = request.WorkerPath ?? Path.Combine(AppContext.BaseDirectory,
                File.Exists(Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "aegn-exporter.exe" : "aegn-exporter"))
                    ? OperatingSystem.IsWindows() ? "aegn-exporter.exe" : "aegn-exporter"
                    : "aegn-exporter.dll");
            if (!Path.IsPathFullyQualified(worker) || !File.Exists(worker))
            {
                throw new FileNotFoundException("缺少邻接的 aegn-exporter，请完整部署应用。", worker);
            }

            var start = new ProcessStartInfo
            {
                FileName = worker.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? ExportExecutable.Resolve("dotnet", Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")) : worker,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = temporary
            };
            if (worker.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                start.ArgumentList.Add(worker);
            }

            var settings = VideoExportSettingsValidator.Normalize(request.ToSettings());
            var job = new ExportWorkerJob(JsonSerializer.SerializeToElement(request.Project, ExportWire.Options),
                Path.GetFullPath(request.ProjectDirectory), temporary, Path.GetExtension(output).ToLowerInvariant(),
                settings.Codec, settings.Preset, settings.Crf, settings.AudioMode, settings.AudioBitrate, ffmpeg,
                settings.EncodingMode, settings.VideoBitrate, request.DecodeMode,
                settings.RateControlMode, ExportWire.VERSION);
            var serializedJob = JsonSerializer.Serialize(job, ExportWire.Options);
            using var process = new Process { StartInfo = start };
            if (!process.Start())
            {
                throw new InvalidOperationException("无法启动导出 worker。");
            }

            using var cancelled = cancellationToken.Register(() =>
            {
                try
                {
                    Kill(process);
                }
                catch (System.ComponentModel.Win32Exception)
                {
                }
            });
            var errors = ReadErrorsAsync(process.StandardError);
            try
            {
                await process.StandardInput.WriteLineAsync(serializedJob.AsMemory(), cancellationToken).ConfigureAwait(false);
                process.StandardInput.Close();
                ExportWorkerMessage? completed = null;
                string? failure = null;
                while (await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
                {
                    if (line.Length > 65536)
                    {
                        throw new InvalidDataException("导出 worker 消息超过限制。");
                    }

                    var message = JsonSerializer.Deserialize<ExportWorkerMessage>(line, ExportWire.Options)
                        ?? throw new InvalidDataException("导出 worker 返回空消息。");
                    if (message.Type == "progress")
                    {
                        progress?.Report(new(message.Frames, new(message.PositionNumerator, message.PositionDenominator), null, "encoding", message.Encoder));
                    }
                    else if (message.Type == "complete")
                    {
                        completed = message;
                    }
                    else if (message.Type == "error")
                    {
                        failure = message.Error;
                    }
                }

                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                var diagnostics = await errors.ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (process.ExitCode != 0 || completed is null)
                {
                    throw new InvalidOperationException(failure ?? $"压制失败：{diagnostics}");
                }

                ValidateCompletedEncoder(request, completed.Encoder);
                ValidateCompletedDecoder(request, completed);
                ValidateCompletedRateControl(request, completed.RateControl);

                var encoded = Path.Combine(temporary, "output" + job.Extension);
                if (!File.Exists(encoded) || new FileInfo(encoded).Length == 0)
                {
                    throw new InvalidDataException("导出 worker 未产生有效成片。");
                }

                cancellationToken.ThrowIfCancellationRequested();
                beforeCommit?.Invoke();
                File.Move(encoded, output, false);
                progress?.Report(new(completed.Frames, MediaTime.Zero, 1, "complete", completed.Encoder));
                return new(output, completed.Frames, completed.Encoder, completed.Decoder, completed.OutputColor, completed.RateControl);
            }
            finally
            {
                try
                {
                    Kill(process);
                }
                finally
                {
                    await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                    await errors.ConfigureAwait(false);
                }
            }
        }
        finally
        {
            Directory.Delete(temporary, true);
        }
    }

    internal static void Validate(VideoExportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ProjectValidator.Validate(request.Project);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProjectDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputPath);
        if (!Path.IsPathFullyQualified(request.ProjectDirectory) || !Path.IsPathFullyQualified(request.OutputPath))
        {
            throw new ArgumentException("项目目录和输出文件必须使用完整路径。", nameof(request));
        }

        if (request.Project.Media is null || request.Project.Width % 2 != 0 || request.Project.Height % 2 != 0)
        {
            throw new ArgumentException("压制需要媒体绑定及偶数视频画布尺寸。", nameof(request));
        }

        if (Path.GetExtension(request.OutputPath).ToLowerInvariant() is not ".mp4" and not ".mkv")
        {
            throw new ArgumentException("首版压制支持 MP4 或 MKV。", nameof(request));
        }

        VideoExportSettingsValidator.Validate(request.ToSettings());
        if (!Enum.IsDefined(request.DecodeMode))
        {
            throw new ArgumentException("不支持的编码参数。", nameof(request));
        }
    }

    internal static void ValidateCompletedEncoder(VideoExportRequest request, string? encoder)
    {
        if (request.EncodingMode != VideoEncodingMode.HARDWARE)
        {
            return;
        }

        var prefix = request.Codec == VideoCodec.Hevc ? "hevc_" : request.Codec == VideoCodec.H264 ? "h264_" : null;
        var hardware = encoder is "h264_videotoolbox" or "hevc_videotoolbox" or "h264_nvenc" or "hevc_nvenc" or
            "h264_qsv" or "hevc_qsv" or "h264_amf" or "hevc_amf";
        if (!hardware || (prefix is not null && !encoder!.StartsWith(prefix, StringComparison.Ordinal)))
        {
            throw new InvalidDataException("GPU 导出 worker 未确认匹配的硬件编码器，可能部署了旧 worker；未提交成片，也不会回退 CPU。");
        }
    }

    internal static void ValidateCompletedDecoder(VideoExportRequest request, ExportWorkerMessage completed)
    {
        var decoder = completed.Decoder;
        var color = completed.OutputColor?.ToMetadata();
        if (decoder is null || !Enum.IsDefined(decoder.ActiveBackend) || decoder.RequestedMode != request.DecodeMode ||
            decoder.Generation == 0 || decoder.DeliveredFrames != completed.Frames || completed.Frames == 0 || color is null ||
            color.Range is null || color.Matrix is null || color.Primaries is null || color.Transfer is null ||
            decoder.HardwareConfirmed != (decoder.ActiveBackend != VideoDecoderBackend.Software) ||
            (request.DecodeMode == VideoDecodeMode.Software && decoder.ActiveBackend != VideoDecoderBackend.Software) ||
            (request.DecodeMode == VideoDecodeMode.Hardware &&
                (!decoder.HardwareConfirmed || decoder.ActiveBackend == VideoDecoderBackend.Software)))
        {
            throw new InvalidDataException("导出 worker 未确认匹配的解码模式、实际后端与有效色彩；未提交成片。");
        }
    }

    internal static void ValidateCompletedRateControl(VideoExportRequest request, VideoRateControlInfo? rateControl)
    {
        var settings = VideoExportSettingsValidator.Normalize(request.ToSettings());
        var mode = settings.RateControlMode;
        if (rateControl is null || rateControl.Mode != mode ||
            (mode == VideoRateControlMode.CRF && (rateControl.VideoBitrate != 0 || rateControl.Crf != settings.Crf)) ||
            (mode != VideoRateControlMode.CRF && (rateControl.Crf != 0 || rateControl.VideoBitrate != settings.VideoBitrate)))
        {
            throw new InvalidDataException("导出 worker 未确认匹配的实际码控模式和参数，可能部署了旧 worker；未提交成片。");
        }
    }

    internal static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    internal static async Task<string> ReadErrorsAsync(StreamReader reader)
    {
        var output = new StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            output.Append(buffer, 0, Math.Min(count, Math.Max(0, 65536 - output.Length)));
        }

        return output.ToString();
    }
}
