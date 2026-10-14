using System.Collections.Immutable;

namespace AegiNext.Application.Presets;

/// <summary>一次不可变导入预览中的展示行和允许的决策；行号也是提交选择的键。</summary>
public sealed record EffectLibraryImportRow
{
    public int Index { get; init; }
    public EffectLibraryItemKind Kind { get; init; }
    public string Name { get; init; } = string.Empty;
    public string SourceName { get; init; } = string.Empty;
    public EffectLibraryImportStatus Status { get; init; }
    public bool CanImport { get; init; }
    public bool CanReplace { get; init; }
    public Guid? TargetId { get; init; }
    public string? DslId { get; init; }
    public string? PreviousDslId { get; init; }
    public bool DslIdChanges => PreviousDslId is not null && !string.Equals(PreviousDslId, DslId, StringComparison.Ordinal);
    public int? ConflictGroupId { get; init; }
    public EffectLibraryImportConflictReason ConflictReason { get; init; }
    public ImmutableArray<string> ConflictingNames { get; init; } = [];
}
