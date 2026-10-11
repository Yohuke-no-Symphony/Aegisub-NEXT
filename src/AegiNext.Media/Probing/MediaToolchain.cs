using System.Text.Json;
using AegiNext.Media.Processes;

namespace AegiNext.Media.Probing;

/// <summary>统一解析随应用分发的固定媒体工具；开发运行允许显式配置及 PATH。</summary>
public static class MediaToolchain
{
    /// <summary>取得用于媒体探测的完整 ffprobe 路径。</summary>
    public static string ResolveFfprobe(string? explicitPath = null) => Resolve("ffprobe", explicitPath);

    /// <summary>取得用于编码与音轨复用的完整 ffmpeg 路径。</summary>
    public static string ResolveFfmpeg(string? explicitPath = null) => Resolve("ffmpeg", explicitPath);

    /// <summary>核验 FFmpeg 可执行文件符合应用内嵌的工具链版本契约。</summary>
    public static async Task ValidateFfmpegAsync(string executablePath, CancellationToken cancellationToken = default)
    {
        var identity = await MediaToolProcess.RunAsync(executablePath, ["-version"], TimeSpan.FromSeconds(10),
            65536, 65536, cancellationToken).ConfigureAwait(false);
        using var resource = typeof(MediaToolchain).Assembly.GetManifestResourceStream("AegiNext.Media.Probing.ffmpeg-toolchain.json")
            ?? throw new InvalidOperationException("缺少内嵌 FFmpeg 工具版本契约。");
        using var manifest = JsonDocument.Parse(resource);
        var first = identity.StandardOutput.Split('\n', 2)[0];
        if (identity.ExitCode != 0 || !manifest.RootElement.GetProperty("acceptedVersionStrings").EnumerateArray()
                .Any(version => first.StartsWith($"ffmpeg version {version.GetString()} ", StringComparison.Ordinal)))
        {
            throw new NotSupportedException("复用音轨所需 FFmpeg 版本不匹配。");
        }
    }

    private static string Resolve(string name, string? explicitPath) => Resolve(name, explicitPath,
        AppContext.BaseDirectory, Environment.GetEnvironmentVariable($"AEGINEXT_{name.ToUpperInvariant()}_PATH"),
        Environment.GetEnvironmentVariable("PATH"), OperatingSystem.IsWindows());

    internal static string Resolve(string name, string? explicitPath, string directory, string? configuredPath,
        string? searchPath, bool windows)
    {
        var executable = name + (windows ? ".exe" : string.Empty);
        var bundled = File.Exists(Path.Combine(directory, "media-runtime.json"));
        if (bundled)
        {
            return Require(Path.Combine(directory, "tools", executable), name);
        }

        var configured = explicitPath ?? configuredPath;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Require(configured, name);
        }

        foreach (var candidateDirectory in new[] { Path.Combine(directory, "tools"), directory }
                     .Concat((searchPath ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)))
        {
            var candidate = candidateDirectory.Trim().Trim('"');
            if (Path.IsPathFullyQualified(candidate) && File.Exists(Path.Combine(candidate, executable)))
            {
                return Path.GetFullPath(Path.Combine(candidate, executable));
            }
        }

        throw new FileNotFoundException($"未找到 {name}；发布包需要 tools/{executable}，开发运行可指定 AEGINEXT_{name.ToUpperInvariant()}_PATH。", executable);
    }

    private static string Require(string path, string name)
    {
        if (!Path.IsPathFullyQualified(path) || !File.Exists(path))
        {
            throw new FileNotFoundException($"需要存在的 {name} 完整路径；请检查应用媒体运行时是否完整。", path);
        }

        return Path.GetFullPath(path);
    }
}
