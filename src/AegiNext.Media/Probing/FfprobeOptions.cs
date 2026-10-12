namespace AegiNext.Media.Probing;

/// <summary>
/// 显式指定探测工具与每次子进程调用的资源边界。
/// </summary>
public sealed record FfprobeOptions
{
    /// <summary>
    /// 创建探测配置；工具路径必须完整，不执行 PATH 回退。
    /// </summary>
    public FfprobeOptions(string executablePath, TimeSpan? timeout = null,
        int maximumOutputCharacters = 16 * 1024 * 1024, int maximumErrorCharacters = 64 * 1024)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        if (!Path.IsPathFullyQualified(executablePath))
        {
            throw new ArgumentException("ffprobe must use an absolute path.", nameof(executablePath));
        }

        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);
        if (effectiveTimeout <= TimeSpan.Zero || effectiveTimeout > TimeSpan.FromMinutes(10))
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "The probing timeout must be between 0 and 10 minutes.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumOutputCharacters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumErrorCharacters);
        ExecutablePath = Path.GetFullPath(executablePath);
        Timeout = effectiveTimeout;
        MaximumOutputCharacters = maximumOutputCharacters;
        MaximumErrorCharacters = maximumErrorCharacters;
    }

    public string ExecutablePath { get; }

    public TimeSpan Timeout { get; }

    public int MaximumOutputCharacters { get; }

    public int MaximumErrorCharacters { get; }
}
