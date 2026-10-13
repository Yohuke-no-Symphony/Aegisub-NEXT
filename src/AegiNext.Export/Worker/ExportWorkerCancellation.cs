using System.Runtime.ExceptionServices;

namespace AegiNext.Export.Worker;

internal sealed class ExportWorkerCancellation : IAsyncDisposable
{
    private readonly CancellationTokenSource cancellation;

    internal ExportWorkerCancellation(TextReader input, CancellationToken cancellationToken)
    {
        cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Completion = Task.Run(() => ReadCommandAsync(input, cancellationToken), CancellationToken.None);
    }

    internal CancellationToken Token => cancellation.Token;
    internal Task Completion { get; }
    internal Exception? Failure { get; private set; }

    internal void ThrowIfCancellationRequested()
    {
        if (Failure is { } error)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }

        Token.ThrowIfCancellationRequested();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await Completion.ConfigureAwait(false);
        cancellation.Dispose();
    }

    private async Task ReadCommandAsync(TextReader input, CancellationToken cancellationToken)
    {
        try
        {
            var command = await input.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (command is null)
            {
                return;
            }

            if (command != ExportWire.CANCEL_COMMAND)
            {
                throw new InvalidDataException("The export worker received an unknown control command.");
            }

            await cancellation.CancelAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await cancellation.CancelAsync().ConfigureAwait(false);
        }
        catch (Exception error)
        {
            Failure = error;
            await cancellation.CancelAsync().ConfigureAwait(false);
        }
    }
}
