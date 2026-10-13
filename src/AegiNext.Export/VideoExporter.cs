using System.Text.Json;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Export.Worker;
using AegiNext.Media.Decoding;

namespace AegiNext.Export;

/// <summary>启动邻接的独立导出 worker，管理取消、进度和不覆盖旧文件的原子提交。</summary>
public sealed class VideoExporter
{
    /// <summary>压制当前工程；失败或取消只删除本次临时文件，已有成片保持原样。</summary>
    public Task<VideoExportResult> ExportAsync(VideoExportRequest request, IProgress<VideoExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return ExportAsync(request, progress, null, cancellationToken);
    }

    /// <summary>压制冻结快照，在原子发布成片前执行应用提供的提交边界。</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Exporter is a service instance used by the application boundary.")]
    public async Task<VideoExportResult> ExportAsync(VideoExportRequest request, IProgress<VideoExportProgress>? progress,
        Action? beforeCommit, CancellationToken cancellationToken)
    {
        Validate(request);
        cancellationToken.ThrowIfCancellationRequested();
        await using var transaction = new ExportOutputTransaction(request.OutputPath);
        try
        {
            var ffmpeg = ExportExecutable.Resolve("ffmpeg", request.FfmpegPath ?? Environment.GetEnvironmentVariable("AEGINEXT_FFMPEG_PATH"));
            var settings = VideoExportSettingsValidator.Normalize(request.ToSettings());
            var job = new ExportWorkerJob(JsonSerializer.SerializeToElement(request.Project, ExportWire.Options),
                Path.GetFullPath(request.ProjectDirectory), transaction.TemporaryDirectory, transaction.Extension,
                ffmpeg, ExportWorkerOperation.BURN_IN,
                new(settings.Codec, settings.Preset, settings.Quality, settings.AudioMode, settings.AudioBitrate,
                    settings.EncodingMode, settings.VideoBitrate, request.DecodeMode, settings.RateControlMode), ExportWire.VERSION);
            var completed = await ExportWorkerClient.RunAsync(JsonSerializer.Serialize(job, ExportWire.Options),
                request.WorkerPath, transaction.TemporaryDirectory, message =>
                {
                    progress?.Report(new(message.Frames, new(message.PositionNumerator, message.PositionDenominator),
                        null, "encoding", message.Encoder));
                }, cancellationToken).ConfigureAwait(false);

            if (completed.Operation != ExportWorkerOperation.BURN_IN || completed.SubtitleMux is not null)
            {
                throw new InvalidDataException("The export worker did not confirm the burn-in operation; the output was not committed.");
            }

            ValidateCompletedEncoder(request, completed.Encoder);
            ValidateCompletedDecoder(request, completed);
            ValidateCompletedRateControl(request, completed.RateControl);
            ValidateCompletedOutput(request, completed.OutputColor);

            transaction.Commit(beforeCommit, cancellationToken);
            progress?.Report(new(completed.Frames, MediaTime.Zero, 1, "complete", completed.Encoder));
            return new(transaction.OutputPath, completed.Frames, completed.Encoder, completed.Decoder,
                completed.OutputColor, completed.RateControl);
        }
        catch (Exception error)
        {
            transaction.RecordFailure(error);
            throw;
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
            throw new ArgumentException("The project directory and output file must use absolute paths.", nameof(request));
        }

        if (request.Project.Media is null || request.Project.Width % 2 != 0 || request.Project.Height % 2 != 0)
        {
            throw new ArgumentException("Video export requires linked media and even canvas dimensions.", nameof(request));
        }

        if (!VideoExportCapabilities.Get(request.Codec).Extensions.Contains(Path.GetExtension(request.OutputPath).ToLowerInvariant()))
        {
            throw new ArgumentException("The output container is incompatible with the selected video codec.", nameof(request));
        }

        ExportMuxer.ValidateAudioCompatibility(Path.GetExtension(request.OutputPath).ToLowerInvariant(), request.AudioMode, null);

        VideoExportSettingsValidator.Validate(request.ToSettings());
        if (!Enum.IsDefined(request.DecodeMode))
        {
            throw new ArgumentException("Unsupported encoding parameters.", nameof(request));
        }
    }

    internal static void ValidateCompletedEncoder(VideoExportRequest request, string? encoder)
    {
        var capability = VideoExportCapabilities.Get(request.Codec);
        if (request.EncodingMode != VideoEncodingMode.HARDWARE)
        {
            if (request.Codec is VideoCodec.Vp8 or VideoCodec.Vp9 or VideoCodec.Av1 && encoder != capability.SoftwareEncoder)
            {
                throw new InvalidDataException("The export worker did not confirm the requested software encoder; the output was not committed.");
            }

            return;
        }
        if (!capability.HardwareEncoders.Contains(encoder))
        {
            throw new InvalidDataException("The GPU export worker did not confirm the requested hardware encoder and may be outdated; the output was not committed, and CPU fallback is disabled.");
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
            throw new InvalidDataException("The export worker did not confirm the requested decode mode, actual backend, and valid color metadata; the output was not committed.");
        }
    }

    internal static void ValidateCompletedRateControl(VideoExportRequest request, VideoRateControlInfo? rateControl)
    {
        var settings = VideoExportSettingsValidator.Normalize(request.ToSettings());
        var mode = settings.RateControlMode;
        var hasQuality = mode is VideoRateControlMode.CRF or VideoRateControlMode.CQ;
        var hasBitrate = mode != VideoRateControlMode.CRF;
        if (rateControl is null || rateControl.Mode != mode || !Enum.IsDefined(rateControl.Verification) ||
            rateControl.BitratePrecision is < 1 or > 1000000 ||
            rateControl.Quality != (hasQuality ? settings.Quality : 0) ||
            !BitrateMatches(rateControl, hasBitrate ? settings.VideoBitrate : 0))
        {
            throw new InvalidDataException("The export worker did not confirm the actual rate-control mode and parameters and may be outdated; the output was not committed.");
        }
    }

    internal static void ValidateCompletedOutput(VideoExportRequest request, VideoExportColor? color)
    {
        if (color is null || color.OutputVerification != 3 || color.Codec is VideoCodec.Auto ||
            !Enum.IsDefined(color.Codec) || request.Codec != VideoCodec.Auto && request.Codec != color.Codec ||
            !VideoExportCapabilities.Get(color.Codec).Extensions.Contains(Path.GetExtension(request.OutputPath).ToLowerInvariant()) ||
            color.PixelFormat is not ("yuv420p" or "yuv420p10le") ||
            color.BitDepth != (color.PixelFormat == "yuv420p10le" ? 10 : 8) ||
            color.ToMetadata() is { } metadata && (metadata.IsPq || metadata.IsHlg) && color.BitDepth < 10)
        {
            throw new InvalidDataException("The export worker did not verify the actual codec and final container format, bit depth, and HDR metadata; the output was not committed.");
        }
    }

    private static bool BitrateMatches(VideoRateControlInfo actual, int expected)
    {
        if (actual.VideoBitrate == expected)
        {
            return true;
        }

        return expected > 0 && actual.VideoBitrate > 0 && actual.Verification == VideoRateControlVerification.RuntimeQueried &&
            Math.Abs((long)actual.VideoBitrate - expected) < actual.BitratePrecision;
    }
}
