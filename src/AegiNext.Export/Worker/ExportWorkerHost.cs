using System.Text.Json;
using AegiNext.Core.Projects;

namespace AegiNext.Export.Worker;

/// <summary>在独立 worker 中执行导出协议，隐藏原生实现与消息细节。</summary>
public static class ExportWorkerHost
{
    /// <summary>处理一个快照请求，由宿主提供工程反序列化，并输出进度及完成或错误回执。</summary>
    public static async Task<int> RunAsync(TextReader input, TextWriter output, TextWriter errors,
        Func<JsonElement, ProjectDocument> deserializeProject, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(deserializeProject);
        var writer = new ExportWorkerMessageWriter(output);
        try
        {
            var line = await input.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                throw new InvalidDataException("导出 worker 需要快照请求。");
            }

            var job = JsonSerializer.Deserialize<ExportWorkerJob>(line, ExportWire.Options)
                ?? throw new InvalidDataException("缺少导出请求。");
            ExportWire.ValidateJob(job);
            var document = deserializeProject(job.Project);
            var outputPath = Path.Combine(job.TemporaryDirectory, "output" + job.Extension);
            var request = new VideoExportRequest(document, job.ProjectDirectory, outputPath)
            {
                Codec = job.Codec,
                Preset = job.Preset,
                Quality = job.Quality,
                AudioMode = job.AudioMode,
                AudioBitrate = job.AudioBitrate,
                FfmpegPath = job.FfmpegPath,
                EncodingMode = job.EncodingMode,
                VideoBitrate = job.VideoBitrate,
                DecodeMode = job.DecodeMode,
                RateControlMode = job.RateControlMode
            };
            var progress = new ExportWorkerProgress(writer);
            var videoPath = Path.Combine(job.TemporaryDirectory, "video.nut");
            var audio = await ExportMuxer.PreflightAsync(request, cancellationToken).ConfigureAwait(false);
            using var session = NativeVideoExport.Run(request, videoPath, progress, cancellationToken);
            var encoded = session.ReadResult(cancellationToken);
            VideoExporter.ValidateCompletedRateControl(request, encoded.RateControl);
            await ExportMuxer.RunAsync(request, session, audio, cancellationToken).ConfigureAwait(false);
            encoded = session.ReadResult(cancellationToken);
            VideoExporter.ValidateCompletedOutput(request, encoded.OutputColor);
            writer.Send(new("complete", encoded.Frames, Encoder: encoded.Encoder, Decoder: encoded.Decoder,
                OutputColor: encoded.OutputColor, RateControl: encoded.RateControl));
            return 0;
        }
        catch (Exception error)
        {
            writer.Send(new("error", Error: error.Message));
            await errors.WriteLineAsync(error.ToString()).ConfigureAwait(false);
            return 1;
        }
    }
}
