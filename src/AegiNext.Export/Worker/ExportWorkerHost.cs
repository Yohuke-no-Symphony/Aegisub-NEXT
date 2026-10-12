using AegiNext.Export.Muxing;
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
                throw new InvalidDataException("The export worker requires a snapshot request.");
            }

            var job = JsonSerializer.Deserialize<ExportWorkerJob>(line, ExportWire.Options)
                ?? throw new InvalidDataException("The export request is missing.");
            ExportWire.ValidateJob(job);
            writer.Operation = job.Operation;
            var document = deserializeProject(job.Project);
            var outputPath = Path.Combine(job.TemporaryDirectory, "output" + job.Extension);
            if (job.Operation == ExportWorkerOperation.SUBTITLE_MUX)
            {
                var muxRequest = new SubtitleMuxRequest(document, job.ProjectDirectory, outputPath) { FfmpegPath = job.FfmpegPath };
                var muxProgress = new SubtitleMuxWorkerProgress(writer);
                var receipt = await SubtitleMuxer.RunAsync(muxRequest, muxProgress, cancellationToken).ConfigureAwait(false);
                writer.Send(new("complete", SubtitleMux: receipt));
                return 0;
            }

            var video = job.Video!;
            var request = new VideoExportRequest(document, job.ProjectDirectory, outputPath)
            {
                Codec = video.Codec,
                Preset = video.Preset,
                Quality = video.Quality,
                AudioMode = video.AudioMode,
                AudioBitrate = video.AudioBitrate,
                FfmpegPath = job.FfmpegPath,
                EncodingMode = video.EncodingMode,
                VideoBitrate = video.VideoBitrate,
                DecodeMode = video.DecodeMode,
                RateControlMode = video.RateControlMode
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
