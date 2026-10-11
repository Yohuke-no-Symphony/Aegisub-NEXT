using AegiNext.Core.Projects;
using AegiNext.Rendering.Projects;

namespace AegiNext.Rendering.Images;

internal sealed class ImageFrameCache(IProjectAssetResolver resolver, long maximumFrameBytes = 128L * 1024 * 1024,
    long maximumEncodedBytes = 64L * 1024 * 1024) : IDisposable
{
    private const long MAXIMUM_METADATA_BYTES = 8L * 1024 * 1024;
    private readonly Dictionary<ProjectAsset, ImageSessionCacheEntry> sessions = [];
    private readonly LinkedList<ProjectAsset> sessionRecency = [];
    private readonly Dictionary<ImageFrameIdentity, ImageFrameCacheEntry> frames = [];
    private readonly LinkedList<ImageFrameIdentity> frameRecency = [];
    private readonly Dictionary<ProjectAsset, ImageMetadata> metadata = [];
    private long retainedSessionBytes;
    private long retainedFrameBytes;
    private long retainedMetadataBytes;

    internal long RetainedFrameBytes => retainedFrameBytes;
    internal long RetainedSessionBytes => retainedSessionBytes;
    internal ulong DecodedFrames { get; private set; }

    internal ImageMetadata GetMetadata(ProjectAsset asset)
    {
        if (metadata.TryGetValue(asset, out var result))
        {
            return result;
        }
        using var lease = OpenSession(asset);
        return lease.Session.Metadata;
    }

    internal ImageFrameLease GetFrame(ImageFrameIdentity identity)
    {
        if (frames.TryGetValue(identity, out var existing))
        {
            frameRecency.Remove(existing.Node);
            frameRecency.AddLast(existing.Node);
            return new(existing.Image, false);
        }

        using var session = OpenSession(identity.Asset);
        var image = session.Session.DecodeFrame(identity.FrameIndex);
        DecodedFrames++;
        var bytes = checked((long)image.Width * image.Height * 4);
        if (bytes > maximumFrameBytes)
        {
            return new(image, true);
        }

        while ((retainedFrameBytes + bytes > maximumFrameBytes || frames.Count >= 4096) && frameRecency.First is { } oldest)
        {
            var removed = frames[oldest.Value];
            frames.Remove(oldest.Value);
            frameRecency.RemoveFirst();
            retainedFrameBytes -= removed.Bytes;
            removed.Image.Dispose();
        }

        frames.Add(identity, new(image, bytes, frameRecency.AddLast(identity)));
        retainedFrameBytes += bytes;
        return new(image, false);
    }

    private ImageSessionLease OpenSession(ProjectAsset asset)
    {
        if (sessions.TryGetValue(asset, out var existing))
        {
            sessionRecency.Remove(existing.Node);
            sessionRecency.AddLast(existing.Node);
            return new(existing.Session, false);
        }

        using var stream = resolver.Open(asset);
        var session = SkiaImageDecoder.Open(stream);
        if (!metadata.ContainsKey(asset))
        {
            var bytes = (long)session.Metadata.FrameCount * 32;
            while (metadata.Count > 0 && (metadata.Count >= 256 || retainedMetadataBytes + bytes > MAXIMUM_METADATA_BYTES))
            {
                var oldest = metadata.First();
                metadata.Remove(oldest.Key);
                retainedMetadataBytes -= (long)oldest.Value.FrameCount * 32;
            }
            metadata.Add(asset, session.Metadata);
            retainedMetadataBytes += bytes;
        }
        if (session.RetainedBytes > maximumEncodedBytes)
        {
            return new(session, true);
        }

        while ((retainedSessionBytes + session.RetainedBytes > maximumEncodedBytes || sessions.Count >= 256) && sessionRecency.First is { } oldest)
        {
            var removed = sessions[oldest.Value];
            sessions.Remove(oldest.Value);
            sessionRecency.RemoveFirst();
            retainedSessionBytes -= removed.Session.RetainedBytes;
            removed.Session.Dispose();
        }

        sessions.Add(asset, new(session, sessionRecency.AddLast(asset)));
        retainedSessionBytes += session.RetainedBytes;
        return new(session, false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var frame in frames.Values)
        {
            frame.Image.Dispose();
        }
        foreach (var session in sessions.Values)
        {
            session.Session.Dispose();
        }
        frames.Clear();
        frameRecency.Clear();
        sessions.Clear();
        sessionRecency.Clear();
        metadata.Clear();
        retainedSessionBytes = 0;
        retainedFrameBytes = 0;
        retainedMetadataBytes = 0;
    }
}
