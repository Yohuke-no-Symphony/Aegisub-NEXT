using SkiaSharp;

namespace AegiNext.Rendering.Images;

internal sealed record ImageFrameCacheEntry(SKImage Image, long Bytes, LinkedListNode<ImageFrameIdentity> Node);
