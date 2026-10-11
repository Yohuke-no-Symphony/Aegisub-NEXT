namespace AegiNext.Export.Tests;

/// <summary>仅在显式提供独立 worker 与媒体工具时运行真实导出。</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ExportTheoryAttribute : TheoryAttribute
{
    private static readonly string[] Variables = ["AEGINEXT_EXPORT_WORKER_PATH", "AEGINEXT_FFMPEG_PATH", "AEGINEXT_FFPROBE_PATH"];

    /// <summary>读取本轮真实导出验证环境。</summary>
    public ExportTheoryAttribute()
    {
        if (Variables
            .Any(name => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))))
        {
            Skip = "真实导出测试需显式提供 AEGINEXT_EXPORT_WORKER_PATH、AEGINEXT_FFMPEG_PATH 和 AEGINEXT_FFPROBE_PATH。";
        }
    }
}
