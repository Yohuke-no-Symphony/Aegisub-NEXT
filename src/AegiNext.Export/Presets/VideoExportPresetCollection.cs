using System.Collections.Immutable;

namespace AegiNext.Export.Presets;

/// <summary>个人库与 .aegiexports 交换文件共用的版本化压制预设根合同。</summary>
public sealed record VideoExportPresetCollection
{
    public const int CURRENT_VERSION = 1;

    public int Version { get; init; } = CURRENT_VERSION;
    public ImmutableArray<VideoExportPreset> Presets { get; init; } = [];
}
