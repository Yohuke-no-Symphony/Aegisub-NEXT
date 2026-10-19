using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Presets;

/// <summary>可跨工程迁移的字幕样式；Style 不持有工程字体标识，字体由交换包和私有字体库独立携带。</summary>
public sealed record SubtitleStylePreset(Guid Id, string Name, SubtitleStyle Style, TimingPostProcessorOptions? TimingPostProcessor = null);
