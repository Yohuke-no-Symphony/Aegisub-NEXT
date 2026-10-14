using System.Diagnostics.CodeAnalysis;

namespace AegiNext.Application.Presets;

/// <summary>由宿主本地化展示的导入冲突原因。</summary>
[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "项目枚举成员采用 ALL_UPPER。")]
public enum EffectLibraryImportConflictReason
{
    NONE,
    DIFFERENT_CONTENT,
    MULTIPLE_TARGETS,
    MULTIPLE_INPUT_VERSIONS,
    BUILTIN_READ_ONLY
}
