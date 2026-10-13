using System.Diagnostics.CodeAnalysis;
using AegiNext.Media.Preview;

namespace AegiNext.Desktop.Rendering;

internal sealed class EffectPresetPreviewFrameCache
{
    private const int ENTRY_OVERHEAD_BYTES = 256;
    private readonly Lock gate = new();
    private readonly long maximumBytes;
    private readonly Dictionary<EffectPresetPreviewCacheKey, LinkedListNode<EffectPresetPreviewCacheEntry>> entries = [];
    private readonly LinkedList<EffectPresetPreviewCacheEntry> order = new();
    private long bytes;
    private long hits;
    private long misses;

    internal EffectPresetPreviewFrameCache(long maximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumBytes);
        this.maximumBytes = maximumBytes;
    }

    internal EffectPresetPreviewCacheStatistics Statistics
    {
        get
        {
            lock (gate)
            {
                return new(entries.Count, bytes, hits, misses);
            }
        }
    }

    internal bool TryGet(EffectPresetPreviewCacheKey key, [NotNullWhen(true)] out SdrVideoFrame? frame)
    {
        lock (gate)
        {
            if (!entries.TryGetValue(key, out var node))
            {
                misses++;
                frame = null;
                return false;
            }
            hits++;
            order.Remove(node);
            order.AddFirst(node);
            frame = node.Value.Frame;
            return true;
        }
    }

    internal void Add(EffectPresetPreviewCacheKey key, SdrVideoFrame frame)
    {
        lock (gate)
        {
            if (entries.Remove(key, out var previous))
            {
                order.Remove(previous);
                bytes -= previous.Value.Bytes;
            }
            var retained = (long)frame.Pixels.Length + ENTRY_OVERHEAD_BYTES;
            if (retained > maximumBytes)
            {
                return;
            }
            while (order.Last is { } oldest && bytes + retained > maximumBytes)
            {
                entries.Remove(oldest.Value.Key);
                bytes -= oldest.Value.Bytes;
                order.RemoveLast();
            }
            entries.Add(key, order.AddFirst(new EffectPresetPreviewCacheEntry(key, frame, retained)));
            bytes += retained;
        }
    }

    internal void Clear()
    {
        lock (gate)
        {
            entries.Clear();
            order.Clear();
            bytes = 0;
        }
    }
}
