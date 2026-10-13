using AegiNext.Core.Timing;
using AegiNext.Media.Preview;

namespace AegiNext.Desktop.Rendering;

internal sealed record EffectPresetPreviewOptions
{
    public string Text { get; init; } = "YOSYMPH666";
    public MediaTime Duration { get; init; } = new(1);
    public int CanvasWidth { get; init; } = 320;
    public int CanvasHeight { get; init; } = 180;
    public string FontFamily { get; init; } = "sans-serif";
    public double FontSize { get; init; } = 32;
    public double Margin { get; init; } = 16;
    public uint BackgroundBgra { get; init; } = 0xFF404040;
    public MediaTime PosterTime => Duration / 2;
    public int FramesPerSecond { get; init; } = 24;
    public long StaticCacheBytes { get; init; } = 32L * 1024 * 1024;
    public int MaximumOutputPixels { get; init; } = 4_194_304;

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Text);
        ArgumentException.ThrowIfNullOrWhiteSpace(FontFamily);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(CanvasWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(CanvasHeight);
        if (Duration <= MediaTime.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(Duration));
        }
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(FramesPerSecond);
        ArgumentOutOfRangeException.ThrowIfNegative(StaticCacheBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumOutputPixels);
        _ = new SdrPreviewOptions(CanvasWidth, CanvasHeight);
        if (!double.IsFinite(FontSize) || FontSize <= 0 || !double.IsFinite(Margin) || Margin < 0 ||
            BackgroundBgra >> 24 != byte.MaxValue)
        {
            throw new ArgumentException("示例样式必须使用有效字号、边距和不透明背景。");
        }
        _ = Duration.ToTimestamp(new(1, FramesPerSecond), MediaTimeRounding.CEILING);
    }
}
