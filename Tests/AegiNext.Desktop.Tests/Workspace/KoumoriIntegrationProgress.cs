namespace AegiNext.Desktop.Tests.Workspace;

internal sealed class KoumoriIntegrationProgress : IProgress<double>
{
    private readonly TaskCompletionSource<double> firstWindow = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal Task<double> FirstWindow => firstWindow.Task;

    public void Report(double value)
    {
        if (value > 0 && value < 1)
        {
            firstWindow.TrySetResult(value);
        }
    }
}
