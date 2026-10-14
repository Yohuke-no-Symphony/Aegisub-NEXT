using AegiNext.Core.Presets;

namespace AegiNext.Desktop.EffectLibrary;

/// <summary>请求保存或应用的独立字幕样式预设。</summary>
public sealed class LibraryStyleEventArgs(SubtitleStylePreset preset) : EventArgs
{
    public SubtitleStylePreset Preset { get; } = preset;
}
