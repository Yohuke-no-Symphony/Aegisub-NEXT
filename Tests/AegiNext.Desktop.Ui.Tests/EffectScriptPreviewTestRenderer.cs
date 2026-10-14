using System.Collections.Concurrent;
using AegiNext.Core.Effects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Rendering;
using AegiNext.Media.Preview;

namespace AegiNext.Desktop.Ui.Tests;

internal sealed class EffectScriptPreviewTestRenderer : IEffectScriptPreviewRenderer
{
    private int disposeCount;

    internal ConcurrentQueue<EffectScriptPreviewRequest> Requests { get; } = new();
    internal ConcurrentQueue<MediaTime> Positions { get; } = new();
    internal int DisposeCount => Volatile.Read(ref disposeCount);

    /// <inheritdoc />
    public SdrVideoFrame Render(EffectScriptPreviewRequest request, MediaTime position, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Enqueue(request);
        Positions.Enqueue(position);
        _ = EffectScriptParser.Parse(request.Input.Source);
        return new(2, 2, new byte[] { 64, 128, 192, 255, 64, 128, 192, 255, 64, 128, 192, 255, 64, 128, 192, 255 });
    }

    /// <inheritdoc />
    public void Dispose() => Interlocked.Increment(ref disposeCount);
}
