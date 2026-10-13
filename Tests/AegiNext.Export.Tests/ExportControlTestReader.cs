namespace AegiNext.Export.Tests;

internal sealed class ExportControlTestReader : TextReader
{
    private readonly TaskCompletionSource<string?> command = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal void Send(string? value)
    {
        command.TrySetResult(value);
    }

    internal void Fail(Exception error)
    {
        command.TrySetException(error);
    }

    /// <inheritdoc />
    public override ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        return new(command.Task.WaitAsync(cancellationToken));
    }
}
