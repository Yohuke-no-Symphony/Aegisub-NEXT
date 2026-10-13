using System.Collections.Concurrent;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Rendering;
using AegiNext.Media.Preview;

namespace AegiNext.Desktop.Ui.Tests;

internal sealed class EffectPresetPanelTestRenderer(Func<EffectPresetPreviewRequest, Exception?>? failure = null) : IEffectPresetPreviewRenderer
{
    private int disposeCount;

    internal ConcurrentQueue<EffectPresetRenderedFrame> Rendered { get; } = new();
    internal int DisposeCount => Volatile.Read(ref disposeCount);

    public SdrVideoFrame Render(EffectPresetPreviewRequest request, MediaTime position, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Rendered.Enqueue(new(request, position));
        if (failure?.Invoke(request) is { } error)
        {
            throw error;
        }
        var marker = (byte)(position.Numerator * 24 / position.Denominator);
        return new(1, 1, [marker, marker, marker, 255]);
    }

    public void Dispose() => Interlocked.Increment(ref disposeCount);
}
