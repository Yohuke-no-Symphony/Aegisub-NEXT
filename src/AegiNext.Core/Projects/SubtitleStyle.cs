using System.Text.Json.Serialization;

namespace AegiNext.Core.Projects;

/// <summary>与渲染器无关的字幕排版及线性颜色样式；尺寸值使用 Scaling 指定的参考单位。</summary>
public sealed record SubtitleStyle
{
    public SubtitleScalingSettings Scaling { get; init; } = new();
    public string FontFamily { get; init; } = "sans-serif";
    public Guid? FontAssetId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SubtitleFontVariant? FontVariant { get; init; }
    public double FontSize { get; init; } = 64;
    public double LetterSpacing { get; init; }
    public SubtitleWrapMode WrapMode { get; init; } = SubtitleWrapMode.GRAPHEME;
    public SceneColor Fill { get; init; } = SceneColor.White;
    public double FillBlur { get; init; }
    public SceneColor Stroke { get; init; } = SceneColor.Black;
    public double StrokeWidth { get; init; } = 2;
    public double StrokeBlur { get; init; }
    public bool Bold { get; init; }
    public bool Italic { get; init; }
    public bool Underline { get; init; }
    public bool Strikethrough { get; init; }
    public TextAlignment Alignment { get; init; } = TextAlignment.BOTTOM_CENTER;
    /// <summary>保留旧工程的独立文字对齐；新编辑通过 Alignment 统一设置对齐。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SubtitleTextAlignment? TextAlign { get; init; }
    public SubtitleMargins Margins { get; init; } = new();
    public SubtitlePosition? Position { get; init; }
    public double LineHeight { get; init; } = 1.2;
    public ScenePoint ShadowOffset { get; init; } = new(2, 2);
    public double ShadowBlur { get; init; } = 2;
    public SceneColor ShadowColor { get; init; } = new(0, 0, 0, 0.6);
}
