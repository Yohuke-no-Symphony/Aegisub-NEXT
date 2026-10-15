using AegiNext.Export;

namespace AegiNext.Desktop.Editing;

/// <summary>输出容器的稳定身份与标准格式名称。</summary>
public sealed class VideoContainerChoice(VideoContainerFormat container)
{
    public VideoContainerFormat Container { get; } = container;

    public string Label { get; } = container switch
    {
        VideoContainerFormat.MP4 => "MP4 (.mp4)",
        VideoContainerFormat.MKV => "MKV (.mkv)",
        VideoContainerFormat.WEBM => "WebM (.webm)",
        _ => throw new ArgumentOutOfRangeException(nameof(container))
    };
}
