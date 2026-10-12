using AegiNext.Core.Projects;

namespace AegiNext.Export;

internal static class SubtitleMuxRequestValidator
{
    internal static void Validate(SubtitleMuxRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ProjectValidator.Validate(request.Project);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProjectDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputPath);
        if (!Path.IsPathFullyQualified(request.ProjectDirectory) || !Path.IsPathFullyQualified(request.OutputPath))
        {
            throw new ArgumentException("项目目录和输出文件必须使用完整路径。", nameof(request));
        }

        if (request.Project.Media is null || request.Project.Subtitles.IsEmpty)
        {
            throw new ArgumentException("内挂字幕需要媒体绑定和至少一条工程字幕。", nameof(request));
        }

        _ = SubtitleMuxCapabilities.GetSubtitleCodec(Path.GetExtension(request.OutputPath));
    }
}
