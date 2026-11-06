namespace AegiNext.Export;

/// <summary>独立音频格式的容器与有效编码参数。</summary>
public static class AudioExportCapabilities
{
    /// <summary>取得格式对应的文件扩展名。</summary>
    public static string GetExtension(AudioFileFormat format) => format switch
    {
        AudioFileFormat.Copy => ".mka",
        AudioFileFormat.Wav => ".wav",
        AudioFileFormat.Flac => ".flac",
        AudioFileFormat.Mp3 => ".mp3",
        AudioFileFormat.Aac => ".m4a",
        AudioFileFormat.Opus => ".opus",
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

    /// <summary>目标格式是否接受有损编码码率。</summary>
    public static bool UsesBitrate(AudioFileFormat format)
    {
        _ = GetExtension(format);
        return format is AudioFileFormat.Mp3 or AudioFileFormat.Aac or AudioFileFormat.Opus;
    }

    /// <summary>取得有损编码允许的最大码率，单位为 bit/s。</summary>
    public static int GetMaximumBitrate(AudioFileFormat format)
    {
        _ = GetExtension(format);
        return format == AudioFileFormat.Mp3 ? 320000 : 512000;
    }

    internal static string GetEncoder(AudioFileFormat format) => format switch
    {
        AudioFileFormat.Copy => "copy",
        AudioFileFormat.Wav => "pcm_s24le",
        AudioFileFormat.Flac => "flac",
        AudioFileFormat.Mp3 => "libmp3lame",
        AudioFileFormat.Aac => "aac",
        AudioFileFormat.Opus => "libopus",
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

    internal static string GetCodec(AudioFileFormat format) => format switch
    {
        AudioFileFormat.Mp3 => "mp3",
        AudioFileFormat.Opus => "opus",
        AudioFileFormat.Copy => throw new ArgumentException("复制模式使用源音轨编码。", nameof(format)),
        _ => GetEncoder(format)
    };

    internal static void ValidateOptions(AudioFileFormat format, int bitrate)
    {
        if (UsesBitrate(format) && (bitrate < 32000 || bitrate > GetMaximumBitrate(format)))
        {
            throw new ArgumentOutOfRangeException(nameof(bitrate), "音频码率超出目标格式支持的范围。");
        }
    }
}
