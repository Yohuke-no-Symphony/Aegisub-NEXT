using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace AegiNext.Core.Presets;

/// <summary>独立于 ProjectDocument 的版本化字幕样式库，也是 .aegistyles 交换文件的根契约。</summary>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "版本化交换文件的集合根契约，名称与既定公共 API 一致。")]
public sealed record SubtitleStylePresetCollection
{
    [SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "项目要求常量使用 ALL_UPPER。")]
    public const int CURRENT_VERSION = 7;
    public int Version { get; init; } = CURRENT_VERSION;
    public ImmutableArray<SubtitleStylePreset> Presets { get; init; } = [];
}
