using AegiNext.Media.Processes;
using System.Globalization;
using System.Security.Cryptography;
using AegiNext.Core.Media;
using AegiNext.Core.Timing;

namespace AegiNext.Media.Probing;

/// <summary>
/// 通过固定版本的受限 ffprobe 进程建立完整实际 PTS 和关键帧索引。
/// </summary>
public sealed class FfprobeVideoTimingProbe
{
    private readonly FfprobeOptions options;

    /// <summary>
    /// 指定工具完整路径、扫描超时和输出预算；不会安装工具或执行 PATH 回退。
    /// </summary>
    public FfprobeVideoTimingProbe(FfprobeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.options = options;
    }

    /// <summary>
    /// 扫描目标视频流全部显示帧；取消、超时和输出超限会终止并回收子进程。
    /// </summary>
    public async Task<VideoTimingIndex> ProbeAsync(string filePath, int streamIndex, MediaTime origin,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentOutOfRangeException.ThrowIfNegative(streamIndex);
        cancellationToken.ThrowIfCancellationRequested();
        var sourcePath = Path.GetFullPath(filePath);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("媒体文件不存在。", sourcePath);
        }

        var sourceInfo = new FileInfo(sourcePath);
        var sourceLength = sourceInfo.Length;
        var sourceModifiedAt = sourceInfo.LastWriteTimeUtc;
        var sourceCreatedAt = sourceInfo.CreationTimeUtc;

        var hash = await HashToolAsync(cancellationToken).ConfigureAwait(false);
        var identityOutput = await RunAsync(
            ["-v", "error", "-of", "json", "-show_program_version", "-show_library_versions"], cancellationToken).ConfigureAwait(false);
        var identity = FfprobeToolchain.ReadIdentity(identityOutput.StandardOutput, options.ExecutablePath, hash);
        var framesOutput = await RunAsync(
            ["-v", "error", "-of", "json", "-show_program_version", "-show_library_versions", "-show_streams", "-show_frames",
                "-select_streams", streamIndex.ToString(CultureInfo.InvariantCulture),
                "-show_entries", "stream=index,codec_type,time_base:frame=stream_index,key_frame,pts,best_effort_timestamp",
                "-protocol_whitelist", "file", "-probesize", "10485760", "-analyzeduration", "10000000", "-i", sourcePath],
            cancellationToken).ConfigureAwait(false);
        if (await HashToolAsync(cancellationToken).ConfigureAwait(false) != hash)
        {
            throw new InvalidDataException("扫描期间媒体工具发生变化，请重新执行。");
        }

        var index = await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var observed = FfprobeToolchain.ReadIdentity(framesOutput.StandardOutput, options.ExecutablePath, hash);
            if (observed.Version != identity.Version || observed.LibraryVersions.Count != identity.LibraryVersions.Count ||
                observed.LibraryVersions.Any(pair => !identity.LibraryVersions.TryGetValue(pair.Key, out var expected) || pair.Value != expected))
            {
                throw new InvalidDataException("扫描期间媒体工具发生变化，请重新执行。");
            }

            return FfprobeVideoTimingJsonReader.Read(framesOutput.StandardOutput, streamIndex, origin, cancellationToken);
        }, cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        sourceInfo.Refresh();
        if (!sourceInfo.Exists || sourceInfo.Length != sourceLength || sourceInfo.LastWriteTimeUtc != sourceModifiedAt ||
            sourceInfo.CreationTimeUtc != sourceCreatedAt)
        {
            throw new InvalidDataException("扫描期间媒体文件发生变化，请重新执行。");
        }

        return index;
    }

    private async Task<MediaToolProcessOutput> RunAsync(string[] arguments, CancellationToken cancellationToken)
    {
        var result = await MediaToolProcess.RunAsync(options.ExecutablePath, arguments, options.Timeout,
            options.MaximumOutputCharacters, options.MaximumErrorCharacters, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 || !string.IsNullOrWhiteSpace(result.StandardError))
        {
            throw new InvalidDataException($"ffprobe 视频帧扫描退出码 {result.ExitCode}：{result.StandardError}");
        }

        return result;
    }

    private async Task<string> HashToolAsync(CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(options.ExecutablePath);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
    }
}
