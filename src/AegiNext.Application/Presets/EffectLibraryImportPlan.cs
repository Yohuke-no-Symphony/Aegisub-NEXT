using System.Collections.Immutable;

namespace AegiNext.Application.Presets;

/// <summary>绑定到仓库与修订的导入预览，保留同一份已读取输入供确认后提交。</summary>
public sealed class EffectLibraryImportPlan
{
    internal EffectLibraryImportPlan(EffectLibraryRepository owner, Guid expectedRevision,
        ImmutableArray<EffectLibraryImportRow> rows, ImmutableArray<EffectLibraryImportEntry> entries)
    {
        Owner = owner;
        ExpectedRevision = expectedRevision;
        Rows = rows;
        Entries = entries;
    }

    public Guid ExpectedRevision { get; }
    public ImmutableArray<EffectLibraryImportRow> Rows { get; }
    internal EffectLibraryRepository Owner { get; }
    internal ImmutableArray<EffectLibraryImportEntry> Entries { get; }
}
