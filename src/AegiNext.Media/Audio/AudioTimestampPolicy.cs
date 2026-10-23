namespace AegiNext.Media.Audio;

/// <summary>选择保留原媒体时间间隙，或使用训练端 FFmpeg async=1 的采样时间语义。</summary>
public enum AudioTimestampPolicy
{
    PRESERVE_DISCONTINUITIES = 0,
    FFMPEG_ASYNC_ONE = 1
}
