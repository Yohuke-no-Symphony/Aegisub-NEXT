using System.Collections.Immutable;

namespace AegiNext.Desktop.EffectLibrary;

/// <summary>请求删除固定的个人特效模板身份，或确认丢弃尚未保存的草稿。</summary>
public sealed class LibraryEffectDeleteEventArgs : EventArgs
{
    /// <summary>以兼容单项调用的方式固定一个已保存身份。</summary>
    public LibraryEffectDeleteEventArgs(Guid id) : this([id])
    {
    }

    /// <summary>固定并去重已保存身份；草稿请求由独立身份标记。</summary>
    public LibraryEffectDeleteEventArgs(IEnumerable<Guid> ids, bool isDraftOnly = false, Guid? draftId = null)
    {
        ArgumentNullException.ThrowIfNull(ids);
        Ids = ids.Distinct().ToImmutableArray();
        IsDraftOnly = isDraftOnly;
        DraftId = draftId;
    }

    public ImmutableArray<Guid> Ids { get; }
    public bool IsDraftOnly { get; }
    public Guid? DraftId { get; }
    public Guid Id => DraftId ?? (Ids.IsEmpty ? Guid.Empty : Ids[0]);
}
