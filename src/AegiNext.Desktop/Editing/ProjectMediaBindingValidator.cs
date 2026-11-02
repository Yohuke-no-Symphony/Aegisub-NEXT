using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controllers;

namespace AegiNext.Desktop.Editing;

internal static class ProjectMediaBindingValidator
{
    internal static void Validate(ProjectDocument document, VideoPreviewMedia media)
    {
        ArgumentNullException.ThrowIfNull(media);
        ProjectValidator.Validate(document);
        var binding = document.Media ?? throw new InvalidDataException("项目没有媒体绑定。");
        if (binding.VideoStreamIndex != media.VideoStreamIndex || binding.AudioStreamIndex != media.AudioStreamIndex)
        {
            throw new InvalidDataException("当前媒体的音视频轨与项目绑定不一致，请重新绑定媒体。");
        }

        // 未报告起点时使用明确的零点映射，保留原始 PTS；不从帧率或时长推断起点。
        var origin = media.Start ?? MediaTime.Zero;
        if (binding.MediaOrigin != origin)
        {
            throw new InvalidDataException("当前媒体的起点与项目绑定不一致，请重新绑定媒体。");
        }

        if (binding.PlaybackOrigin is { } savedPlaybackOrigin && media.PlaybackOrigin is { } probedPlaybackOrigin &&
            savedPlaybackOrigin != probedPlaybackOrigin)
        {
            throw new InvalidDataException("当前媒体的播放零点与项目绑定不一致，请重新绑定媒体。");
        }

        if (media.IsAudioOnly)
        {
            return;
        }

        if (media.VideoWidth is not > 0 || media.VideoHeight is not > 0 ||
            document.Width != media.VideoWidth || document.Height != media.VideoHeight)
        {
            throw new InvalidDataException("项目画布尺寸与当前媒体不一致或媒体尺寸未知，请重新绑定媒体。");
        }
    }
}
