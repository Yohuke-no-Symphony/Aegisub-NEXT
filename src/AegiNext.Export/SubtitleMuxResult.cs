namespace AegiNext.Export;

/// <summary>已原子提交的封装结果；记录复制流数量以及新增字幕的输出索引和编码。</summary>
public sealed record SubtitleMuxResult(string OutputPath, int VideoStreamCount, int AudioStreamCount,
    int PreservedSubtitleStreamCount, int AddedSubtitleStreamIndex, string SubtitleCodec);
