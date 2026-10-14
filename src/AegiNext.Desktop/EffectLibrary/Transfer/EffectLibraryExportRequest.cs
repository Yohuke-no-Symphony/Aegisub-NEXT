using System.Collections.Immutable;
using AegiNext.Application.Presets;
using AegiNext.Core.Presets;

namespace AegiNext.Desktop.EffectLibrary.Transfer;

/// <summary>冻结当前导出操作的保存素材与交换格式。</summary>
internal sealed record EffectLibraryExportRequest(ImmutableArray<SubtitleStylePreset> Styles,
    ImmutableArray<EffectScriptPreset> Effects, EffectLibraryExportFormat Format);
