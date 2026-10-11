using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Editing;

/// <summary>字幕自动对齐与共享图层位置草稿之间的适配，保留字幕模板的持久化类型。</summary>
public sealed class SubtitlePositionDraft : LayerPositionDraft
{
    /// <summary>以字幕自动位置初始化草稿。</summary>
    public SubtitlePositionDraft()
    {
        Load(new());
    }

    /// <summary>加载显式位置或自动对齐派生的位置；resolved 保留实际字幕基线。</summary>
    public void Load(SubtitleStyle style, SubtitlePosition? resolved = null, bool canCustomize = true,
        LayerPositionDraftGeometry? geometry = null)
    {
        LoadCore(style.Position ?? resolved ?? SubtitlePosition.FromAlignment(style.Alignment, style.Margins),
            style.Position is not null, canCustomize, geometry);
    }

    /// <summary>取得共享运算生成的字幕位置，保留原有序列化类型。</summary>
    public override SubtitlePosition? CreatePosition() => (SubtitlePosition?)base.CreatePosition();
}
