namespace AegiNext.Export;

/// <summary>实际码控回执的证据强度，避免将配置与初始化冒充驱动读回。</summary>
public enum VideoRateControlVerification
{
    /// <summary>参数已被匹配编码器消费，且编码器初始化成功。</summary>
    ConfiguredAndInitialized = 1,

    /// <summary>由编码会话或驱动查询确认有效参数。</summary>
    RuntimeQueried = 2
}
