using AegiNext.Media.Processes;
using System.Security.Cryptography;

namespace AegiNext.Media.Probing;

/// <summary>
/// 以受限独立进程探测本地媒体，完整保留流级事实与工具来源。
/// </summary>
public sealed class FfprobeMediaProbe
{
    private readonly FfprobeOptions options;

    /// <summary>
    /// 创建探测器；不会查找、安装或替换工具。
    /// </summary>
    public FfprobeMediaProbe(FfprobeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.options = options;
    }

    /// <summary>
    /// 验证工具版本后读取本地文件流信息；取消、超时和输出超限均停止并等待子进程退出。
    /// </summary>
    public async Task<MediaProbeReport> ProbeAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        cancellationToken.ThrowIfCancellationRequested();
        var sourcePath = Path.GetFullPath(filePath);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("媒体文件不存在。", sourcePath);
        }

        var hash = await HashToolAsync(cancellationToken).ConfigureAwait(false);
        var identityOutput = await RunAsync(["-v", "error", "-of", "json", "-show_program_version", "-show_library_versions"], cancellationToken).ConfigureAwait(false);
        var identity = FfprobeToolchain.ReadIdentity(identityOutput.StandardOutput, options.ExecutablePath, hash);
        var mediaOutput = await RunAsync(
            ["-v", "error", "-of", "json", "-show_program_version", "-show_library_versions", "-show_streams", "-show_format",
                "-protocol_whitelist", "file", "-probesize", "10485760", "-analyzeduration", "10000000", "-i", sourcePath], cancellationToken).ConfigureAwait(false);
        var observed = FfprobeToolchain.ReadIdentity(mediaOutput.StandardOutput, options.ExecutablePath, hash);
        if (observed.Version != identity.Version ||
            observed.LibraryVersions.Count != identity.LibraryVersions.Count ||
            observed.LibraryVersions.Any(pair => !identity.LibraryVersions.TryGetValue(pair.Key, out var expected) || pair.Value != expected) ||
            await HashToolAsync(cancellationToken).ConfigureAwait(false) != hash)
        {
            throw new InvalidDataException("探测期间媒体工具发生变化，请重新执行。");
        }

        return new(sourcePath, FfprobeJsonReader.Read(mediaOutput.StandardOutput), identity,
            string.Join(Environment.NewLine, new[] { identityOutput.StandardError, mediaOutput.StandardError }.Where(value => value.Length > 0)));
    }

    private async Task<MediaToolProcessOutput> RunAsync(string[] arguments, CancellationToken cancellationToken)
    {
        var result = await MediaToolProcess.RunAsync(options.ExecutablePath, arguments, options.Timeout,
            options.MaximumOutputCharacters, options.MaximumErrorCharacters, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new InvalidDataException($"ffprobe 退出码 {result.ExitCode}：{result.StandardError}");
        }

        return result;
    }

    private async Task<string> HashToolAsync(CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(options.ExecutablePath);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
    }
}
