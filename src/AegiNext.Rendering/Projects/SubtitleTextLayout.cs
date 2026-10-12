using System.Collections.Immutable;
using SkiaSharp;

namespace AegiNext.Rendering.Projects;

/// <summary>渲染器与字幕编辑器共享的不可变局部排版几何，不拥有原生资源且无需释放。</summary>
public sealed class SubtitleTextLayout
{
    private readonly SKRect emptyCaret;

    internal SubtitleTextLayout(string text, SKRect bounds, SKPoint basePosition, SKPoint pivot, bool hasInk,
        ImmutableArray<SubtitleTextRunGeometry> runs, ImmutableArray<SubtitleGraphemeGeometry> graphemes, SKRect emptyCaret)
    {
        Text = text;
        Bounds = bounds;
        BasePosition = basePosition;
        Pivot = pivot;
        HasInk = hasInk;
        Runs = runs;
        Graphemes = graphemes;
        this.emptyCaret = emptyCaret;
    }

    public string Text { get; }
    public SKRect Bounds { get; }
    public SKPoint BasePosition { get; }
    public SKPoint Pivot { get; }
    public bool HasInk { get; }
    public ImmutableArray<SubtitleTextRunGeometry> Runs { get; }
    public ImmutableArray<SubtitleGraphemeGeometry> Graphemes { get; }

    /// <summary>返回距离指针最近的字素及插入侧；所有结果均位于完整字素边界。</summary>
    public SubtitleTextHit HitTest(SKPoint point)
    {
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(point));
        }
        if (Graphemes.IsEmpty)
        {
            return new(0, 0, 0, false);
        }
        var nearest = Graphemes[0];
        var distance = float.PositiveInfinity;
        foreach (var grapheme in Graphemes)
        {
            var dx = Math.Max(Math.Max(grapheme.Bounds.Left - point.X, 0), point.X - grapheme.Bounds.Right);
            var dy = Math.Max(Math.Max(grapheme.Bounds.Top - point.Y, 0), point.Y - grapheme.Bounds.Bottom);
            var next = Math.Min(dx * dx + dy * dy,
                Math.Min(CaretDistance(point, grapheme.LeadingCaret), CaretDistance(point, grapheme.TrailingCaret)));
            if (grapheme.UntransformedBounds is { } original && grapheme.LocalToVisible.TryInvert(out var inverse) &&
                original.Contains(inverse.MapPoint(point)))
            {
                next = 0;
            }
            if (next < distance)
            {
                nearest = grapheme;
                distance = next;
            }
        }
        var trailing = CaretDistance(point, nearest.TrailingCaret) < CaretDistance(point, nearest.LeadingCaret);
        return new(trailing ? nearest.Utf16Start + nearest.Utf16Length : nearest.Utf16Start,
            nearest.Utf16Start, nearest.Utf16Length, trailing);
    }

    private static float CaretDistance(SKPoint point, SKRect caret)
    {
        var dx = point.X - caret.Left;
        var dy = Math.Max(Math.Max(caret.Top - point.Y, 0), point.Y - caret.Bottom);
        return dx * dx + dy * dy;
    }

    /// <summary>获取完整字素边界处的光标；软换行边界优先使用下一排版行的起点。</summary>
    public SKRect GetCaretBounds(int utf16Offset)
    {
        if (utf16Offset < 0 || utf16Offset > Text.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(utf16Offset));
        }
        foreach (var grapheme in Graphemes)
        {
            if (grapheme.Utf16Start == utf16Offset)
            {
                return grapheme.LeadingCaret;
            }
        }
        if (utf16Offset == Text.Length)
        {
            return Graphemes.IsEmpty ? emptyCaret : Graphemes[^1].TrailingCaret;
        }
        throw new ArgumentOutOfRangeException(nameof(utf16Offset), "The caret cannot split a grapheme.");
    }

    /// <summary>获取字素安全选区的逐行矩形，不合并不同排版行。</summary>
    public ImmutableArray<SKRect> GetSelectionRects(int utf16Start, int utf16Length)
    {
        if (utf16Length < 0 || (long)utf16Start + utf16Length > Text.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(utf16Length));
        }
        GetCaretBounds(utf16Start);
        GetCaretBounds(utf16Start + utf16Length);
        if (utf16Length == 0)
        {
            return [];
        }
        var result = ImmutableArray.CreateBuilder<SKRect>();
        var line = -1;
        foreach (var grapheme in Graphemes)
        {
            if (grapheme.Utf16Start < utf16Start || grapheme.Utf16Start >= utf16Start + utf16Length)
            {
                continue;
            }
            var bounds = grapheme.Bounds;
            if (bounds.Width <= 0)
            {
                bounds.Right = bounds.Left + Math.Max(1, bounds.Height / 8);
            }
            if (line == grapheme.LineIndex)
            {
                result[^1] = SKRect.Union(result[^1], bounds);
            }
            else
            {
                result.Add(bounds);
                line = grapheme.LineIndex;
            }
        }
        return result.ToImmutable();
    }
}
