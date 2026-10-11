using AegiNext.Export;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AegiNext.Desktop.Editing;

/// <summary>编码格式的稳定枚举身份与可刷新显示名称。</summary>
public sealed class VideoCodecChoice(VideoCodec codec) : ObservableObject
{
    private string label = string.Empty;

    public VideoCodec Codec { get; } = codec;
    public string Label => label;

    internal void UpdateLabel(string value)
    {
        SetProperty(ref label, value, nameof(Label));
    }
}
