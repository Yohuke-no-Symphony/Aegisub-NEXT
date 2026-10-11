namespace AegiNext.Export;

/// <summary>视频编码执行方式；硬件模式不可用时明确失败，不回退软件编码。</summary>
public enum VideoEncodingMode
{
    SOFTWARE,
    HARDWARE
}
