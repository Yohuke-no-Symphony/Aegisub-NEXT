namespace AegiNext.Export.Worker;

internal sealed record SubtitleMuxReceipt(int VideoStreamCount, int AudioStreamCount,
    int PreservedSubtitleStreamCount, int AddedSubtitleStreamIndex, string SubtitleCodec, bool OutputVerified);
