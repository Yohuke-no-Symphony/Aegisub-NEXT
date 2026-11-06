using AegiNext.Core.Projects;

namespace AegiNext.Export;

internal static class AudioExportRequestValidator
{
    internal static void Validate(AudioExportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ProjectValidator.Validate(request.Project);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProjectDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputPath);
        if (!Path.IsPathFullyQualified(request.ProjectDirectory) || !Path.IsPathFullyQualified(request.OutputPath))
        {
            throw new ArgumentException("项目目录和输出文件必须使用完整路径。", nameof(request));
        }

        if (request.Project.Media is null)
        {
            throw new ArgumentException("导出音轨需要绑定源媒体。", nameof(request));
        }

        AudioExportCapabilities.ValidateOptions(request.Format, request.Bitrate);
        if (!Path.GetExtension(request.OutputPath).Equals(AudioExportCapabilities.GetExtension(request.Format), StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("输出文件扩展名与音频格式不一致。", nameof(request));
        }
    }
}
