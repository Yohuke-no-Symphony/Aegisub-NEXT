using AegiNext.Core.Projects;

namespace AegiNext.Rendering.Projects;

/// <summary>包含资源身份的图片渲染错误；预览可隔离该资源，导出必须报告失败。</summary>
public sealed class ProjectImageRenderException : IOException
{
    /// <summary>保留具体解码或文件异常及失败资源身份。</summary>
    public ProjectImageRenderException(ProjectAsset asset, Exception innerException)
        : base($"Image asset {asset.Id} ({Path.GetFileName(asset.ExternalPath ?? asset.RelativePath)}) could not be rendered: {innerException.Message}", innerException)
    {
        AssetId = asset.Id;
    }

    public Guid AssetId { get; }
}
