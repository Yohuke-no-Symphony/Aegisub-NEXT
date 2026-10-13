using System.Collections.Concurrent;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Rendering;
using AegiNext.Media.Preview;

namespace AegiNext.Desktop.Tests;

internal sealed class EffectPresetPreviewTestRenderer(Func<EffectPresetPreviewRequest, MediaTime, CancellationToken, SdrVideoFrame>? render = null)
    : IEffectPresetPreviewRenderer
{
    private int rendering;

    internal int CreationThread { get; } = Environment.CurrentManagedThreadId;
    internal int? DisposalThread { get; private set; }
    internal int MaximumConcurrency { get; private set; }
    internal ConcurrentQueue<(EffectPresetPreviewRequest Request, MediaTime Position, int Thread)> Calls { get; } = new();

    /// <inheritdoc />
    public SdrVideoFrame Render(EffectPresetPreviewRequest request, MediaTime position, CancellationToken cancellationToken)
    {
        var concurrency = Interlocked.Increment(ref rendering);
        MaximumConcurrency = Math.Max(MaximumConcurrency, concurrency);
        Calls.Enqueue((request, position, Environment.CurrentManagedThreadId));
        try
        {
            return render?.Invoke(request, position, cancellationToken) ?? new SdrVideoFrame(1, 1, [0, 0, 0, 255]);
        }
        finally
        {
            Interlocked.Decrement(ref rendering);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        DisposalThread = Environment.CurrentManagedThreadId;
    }
}
