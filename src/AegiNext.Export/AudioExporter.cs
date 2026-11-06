using System.Text.Json;
using AegiNext.Core.Timing;
using AegiNext.Export.Worker;

namespace AegiNext.Export;

/// <summary>通过独立 worker 导出单条音轨，验证后原子提交输出文件。</summary>
public sealed class AudioExporter
{
    /// <summary>导出完整源音轨；失败或取消不会发布不完整文件。</summary>
    public Task<AudioExportResult> ExportAsync(AudioExportRequest request, IProgress<AudioExportProgress>? progress = null,
        CancellationToken cancellationToken = default) => ExportAsync(request, progress, null, cancellationToken);

    /// <summary>导出完整源音轨，并在发布文件前进入应用提交边界。</summary>
    public async Task<AudioExportResult> ExportAsync(AudioExportRequest request, IProgress<AudioExportProgress>? progress,
        Action? beforeCommit, CancellationToken cancellationToken)
    {
        AudioExportRequestValidator.Validate(request);
        cancellationToken.ThrowIfCancellationRequested();
        await using var transaction = new ExportOutputTransaction(request.OutputPath);
        try
        {
            var ffmpeg = ExportExecutable.Resolve("ffmpeg", request.FfmpegPath);
            var job = new ExportWorkerJob(JsonSerializer.SerializeToElement(request.Project, ExportWire.Options),
                Path.GetFullPath(request.ProjectDirectory), transaction.TemporaryDirectory, transaction.Extension,
                ffmpeg, ExportWorkerOperation.AUDIO, ProtocolVersion: ExportWire.VERSION)
            {
                Audio = new(request.Format, request.Bitrate)
            };
            var completed = await ExportWorkerClient.RunAsync(JsonSerializer.Serialize(job, ExportWire.Options),
                request.WorkerPath, transaction.TemporaryDirectory, message =>
                {
                    progress?.Report(new(new(message.PositionNumerator, message.PositionDenominator), message.Fraction,
                        message.Stage ?? "encoding"));
                }, cancellationToken).ConfigureAwait(false);
            var receipt = ValidateCompleted(request, completed);
            transaction.Commit(beforeCommit, cancellationToken);
            progress?.Report(new(MediaTime.Zero, 1, "complete"));
            return new(transaction.OutputPath, receipt.SourceStreamIndex, receipt.Codec, receipt.SampleRate, receipt.Channels);
        }
        catch (Exception error)
        {
            transaction.RecordFailure(error);
            throw;
        }
    }

    internal static AudioExportReceipt ValidateCompleted(AudioExportRequest request, ExportWorkerMessage completed)
    {
        var receipt = completed.Audio;
        if (completed.Operation != ExportWorkerOperation.AUDIO || receipt is null || !receipt.OutputVerified ||
            receipt.SourceStreamIndex < 0 || string.IsNullOrWhiteSpace(receipt.Codec) || receipt.SampleRate <= 0 || receipt.Channels <= 0 ||
            request.Project.Media?.AudioStreamIndex is { } selected && receipt.SourceStreamIndex != selected ||
            request.Format != AudioFileFormat.Copy && receipt.Codec != AudioExportCapabilities.GetCodec(request.Format) ||
            completed.Frames != 0 || completed.Encoder is not null || completed.Decoder is not null ||
            completed.OutputColor is not null || completed.RateControl is not null || completed.OutputGeometry is not null ||
            completed.SubtitleMux is not null)
        {
            throw new InvalidDataException("导出 worker 未确认音轨及最终音频校验；未提交输出。");
        }

        return receipt;
    }
}
