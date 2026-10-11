namespace AegiNext.Core.Projects;

/// <summary>图片资源与显示矩形；Fit 区分留边适配、覆盖裁剪和非等比拉伸。</summary>
public sealed record LayerImage(Guid AssetId, double Width, double Height, ImageFit Fit = ImageFit.FIT)
{
    public NormalizedImageCrop Crop { get; init; } = new();
    public ImagePlayback? Playback { get; init; }
}
