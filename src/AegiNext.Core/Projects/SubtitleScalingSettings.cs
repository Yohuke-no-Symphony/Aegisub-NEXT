namespace AegiNext.Core.Projects;

/// <summary>字幕尺寸值的设计分辨率及宽高匹配权重；零按宽度，一按高度。</summary>
public sealed record SubtitleScalingSettings
{
    public int ReferenceWidth { get; init; } = 1920;
    public int ReferenceHeight { get; init; } = 1080;
    public double MatchWidthOrHeight { get; init; } = 0.5;
}
