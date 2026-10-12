using System.Collections.Immutable;

namespace AegiNext.Core.Projects;

/// <summary>随工程保存的时间轴视图状态，不参与字幕或特效内容的撤销历史。</summary>
public sealed record TimelineViewState
{
    private const int MAXIMUM_VIEW_IDENTITIES = 100000;

    /// <summary>独立展开的特效块；默认全部收起，暂时不存在的目标允许保留。</summary>
    public ImmutableArray<TimelineEffectClipId> ExpandedAnimationClips { get; init; } = [];
    /// <summary>仅用于读取旧工程的属性行折叠状态。</summary>
    public ImmutableArray<TimelineAnimationRowId> CollapsedAnimationRows { get; init; } = [];
    /// <summary>整体折叠的字幕轨道、场景层及组身份；暂时不存在的身份允许保留。</summary>
    public ImmutableArray<Guid> CollapsedTrackIds { get; init; } = [];

    /// <summary>验证视图身份与集合预算；暂时不存在的所属实体或属性允许保留。</summary>
    public void Validate()
    {
        if (ExpandedAnimationClips.IsDefault || ExpandedAnimationClips.Length > MAXIMUM_VIEW_IDENTITIES)
        {
            throw new InvalidDataException("The timeline expanded effect-block collection is invalid or too large.");
        }

        var effectIdentities = new HashSet<TimelineEffectClipId>();
        foreach (var clip in ExpandedAnimationClips)
        {
            if (clip is null || clip.LayerId == Guid.Empty || !effectIdentities.Add(clip))
            {
                throw new InvalidDataException("A timeline expanded effect-block identity is invalid or duplicated.");
            }

            SubtitleAnimationTargetValidation.ValidateIdentity(clip.Target);
        }

        if (CollapsedTrackIds.IsDefault || CollapsedTrackIds.Length > MAXIMUM_VIEW_IDENTITIES)
        {
            throw new InvalidDataException("The timeline fully collapsed track collection is invalid or too large.");
        }

        var trackIdentities = new HashSet<Guid>();
        foreach (var id in CollapsedTrackIds)
        {
            if (id == Guid.Empty || !trackIdentities.Add(id))
            {
                throw new InvalidDataException("A timeline fully collapsed track identity is invalid or duplicated.");
            }
        }

        if (CollapsedAnimationRows.IsDefault || CollapsedAnimationRows.Length > MAXIMUM_VIEW_IDENTITIES)
        {
            throw new InvalidDataException("The timeline collapsed row collection is invalid or too large.");
        }

        var identities = new HashSet<TimelineAnimationRowId>();
        foreach (var row in CollapsedAnimationRows)
        {
            if (row is null || row.OwnerId == Guid.Empty || !Enum.IsDefined(row.Scope) ||
                !AnimationPropertyMetadata.CurrentProperties.Contains(row.Property) || !identities.Add(row) ||
                row.TextRangeId == Guid.Empty || !Enum.IsDefined(row.State) ||
                row.TextRangeId.HasValue && !AnimationPropertyMetadata.IsTextRangeProperty(row.Property) ||
                row.State != SubtitleAnimationState.NORMAL && !AnimationPropertyMetadata.IsSubtitleVisualProperty(row.Property))
            {
                throw new InvalidDataException("A timeline collapsed row identity is invalid or duplicated.");
            }
        }
    }
}
