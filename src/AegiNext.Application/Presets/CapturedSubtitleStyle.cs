using System.Collections.Immutable;
using AegiNext.Core.Fonts;
using AegiNext.Core.Presets;

namespace AegiNext.Application.Presets;

/// <summary>捕获的样式参数及可选的完整工程字体族；非空字体族与样式作为同一个库事务保存。</summary>
public sealed record CapturedSubtitleStyle(SubtitleStylePreset Preset, ImmutableArray<FontFamilyBundle> FontFamilies);
