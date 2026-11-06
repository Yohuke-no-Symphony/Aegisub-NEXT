namespace AegiNext.Export;

/// <summary>独立音轨的原样复制或目标编码格式。</summary>
public enum AudioFileFormat
{
    Copy,
    /// <summary>24 位整数 PCM WAV，保持源采样率和声道。</summary>
    Wav,
    /// <summary>整数 FLAC，保持源采样率和声道。</summary>
    Flac,
    Mp3,
    Aac,
    Opus
}
