using System.Collections.Concurrent;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Rendering;
using AegiNext.Media.Preview;

namespace AegiNext.Desktop.Tests;

internal sealed class EffectScriptPreviewTestRenderer(
    Func<EffectScriptPreviewRequest, MediaTime, CancellationToken, SdrVideoFrame>? render = null) : IEffectScriptPreviewRenderer
{
    private int rendering;

    internal int CreationThread { get; } = Environment.CurrentManagedThreadId;
    internal int? DisposalThread { get; private set; }
    internal int DisposalCount { get; private set; }
    internal int MaximumConcurrency { get; private set; }
    internal ConcurrentQueue<(EffectScriptPreviewRequest Request, MediaTime Position, int Thread)> Calls { get; } = new();

    /// <inheritdoc />
    public SdrVideoFrame Render(EffectScriptPreviewRequest request, MediaTime position, CancellationToken cancellationToken)
    {
        var concurrency = Interlocked.Increment(ref rendering);
        MaximumConcurrency = Math.Max(MaximumConcurrency, concurrency);
        Calls.Enqueue((request, position, Environment.CurrentManagedThreadId));
        try
        {
            return render?.Invoke(request, position, cancellationToken) ?? new(1, 1, [0, 0, 0, 255]);
        }
        finally
        {
            Interlocked.Decrement(ref rendering);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        DisposalCount++;
        DisposalThread = Environment.CurrentManagedThreadId;
    }
}
