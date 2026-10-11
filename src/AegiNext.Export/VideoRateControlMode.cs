namespace AegiNext.Export;

/// <summary>视频质量或码率控制方式；自动模式用于兼容已有压制请求。</summary>
public enum VideoRateControlMode
{
    /// <summary>软件编码使用 CRF，硬件编码使用 VBR。</summary>
    AUTOMATIC = 0,

    /// <summary>按质量压制，仅适用于软件编码。</summary>
    CRF = 1,

    /// <summary>使用可变目标码率。</summary>
    VBR = 2,

    /// <summary>使用恒定目标码率。</summary>
    CBR = 3,

    /// <summary>VP8 软件编码在目标码率约束下保持质量。</summary>
    CQ = 4
}
