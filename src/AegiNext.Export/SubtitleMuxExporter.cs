using System.Text.Json;
using AegiNext.Core.Timing;
using AegiNext.Export.Worker;

namespace AegiNext.Export;

/// <summary>通过独立 worker 复制原媒体流并追加字幕，验证后原子发布新文件。</summary>
public sealed class SubtitleMuxExporter
{
    /// <summary>仅封装冻结工程快照；失败或取消不会发布不完整文件。</summary>
    public Task<SubtitleMuxResult> ExportAsync(SubtitleMuxRequest request, IProgress<SubtitleMuxProgress>? progress = null,
        CancellationToken cancellationToken = default) => ExportAsync(request, progress, null, cancellationToken);

    /// <summary>仅封装冻结工程快照，在原子发布前执行应用提交边界。</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Exporter is a service instance used by the application boundary.")]
    public async Task<SubtitleMuxResult> ExportAsync(SubtitleMuxRequest request, IProgress<SubtitleMuxProgress>? progress,
        Action? beforeCommit, CancellationToken cancellationToken)
    {
        SubtitleMuxRequestValidator.Validate(request);
        cancellationToken.ThrowIfCancellationRequested();
        await using var transaction = new ExportOutputTransaction(request.OutputPath);
        try
        {
            var ffmpeg = ExportExecutable.Resolve("ffmpeg", request.FfmpegPath ?? Environment.GetEnvironmentVariable("AEGINEXT_FFMPEG_PATH"));
            var job = new ExportWorkerJob(JsonSerializer.SerializeToElement(request.Project, ExportWire.Options),
                Path.GetFullPath(request.ProjectDirectory), transaction.TemporaryDirectory, transaction.Extension,
                ffmpeg, ExportWorkerOperation.SUBTITLE_MUX, ProtocolVersion: ExportWire.VERSION);
            var completed = await ExportWorkerClient.RunAsync(JsonSerializer.Serialize(job, ExportWire.Options),
                request.WorkerPath, transaction.TemporaryDirectory, message =>
                {
                    progress?.Report(new(new(message.PositionNumerator, message.PositionDenominator), message.Fraction,
                        message.Stage ?? "muxing"));
                }, cancellationToken).ConfigureAwait(false);
            var receipt = ValidateCompleted(request, completed);
            transaction.Commit(beforeCommit, cancellationToken);
            progress?.Report(new(MediaTime.Zero, 1, "complete"));
            return new(transaction.OutputPath, receipt.VideoStreamCount, receipt.AudioStreamCount,
                receipt.PreservedSubtitleStreamCount, receipt.AddedSubtitleStreamIndex, receipt.SubtitleCodec);
        }
        catch (Exception error)
        {
            transaction.RecordFailure(error);
            throw;
        }
    }

    internal static SubtitleMuxReceipt ValidateCompleted(SubtitleMuxRequest request, ExportWorkerMessage completed)
    {
        var receipt = completed.SubtitleMux;
        if (completed.Operation != ExportWorkerOperation.SUBTITLE_MUX || receipt is null || !receipt.OutputVerified ||
            receipt.VideoStreamCount <= 0 || receipt.AudioStreamCount < 0 || receipt.PreservedSubtitleStreamCount < 0 ||
            receipt.AddedSubtitleStreamIndex < receipt.VideoStreamCount + receipt.AudioStreamCount + receipt.PreservedSubtitleStreamCount ||
            receipt.SubtitleCodec != SubtitleMuxCapabilities.GetSubtitleCodec(Path.GetExtension(request.OutputPath)) ||
            completed.Frames != 0 || completed.Encoder is not null || completed.Decoder is not null ||
            completed.OutputColor is not null || completed.RateControl is not null)
        {
            throw new InvalidDataException("导出 worker 未确认仅封装操作及最终媒体、字幕校验；未提交输出。");
        }

        return receipt;
    }
}
