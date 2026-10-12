namespace AegiNext.Core.Projects;

/// <summary>字幕在样式参考单位中的左、右和共享垂直边距。</summary>
public readonly record struct SubtitleMargins(double Left, double Right, double Vertical)
{
    /// <summary>保持字幕样式默认的 40 像素边距。</summary>
    public SubtitleMargins() : this(40, 40, 40)
    {
    }
}
