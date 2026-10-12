using AegiNext.SubtitleFormats;

namespace AegiNext.Cli.Tests;

internal sealed class RecordingSubtitleConversionService : ISubtitleConversionService
{
    internal SubtitleFormatWriteResult Result { get; init; } = new("已转换 😀", []);
    internal Action? BeforeReturn { get; init; }
    internal Exception? Failure { get; init; }
    internal int Calls { get; private set; }
    internal string? Input { get; private set; }
    internal string? Target { get; private set; }

    public Task<SubtitleFormatWriteResult> ConvertAsync(string inputPath, string target, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls++;
        Input = inputPath;
        Target = target;
        if (Failure is not null)
        {
            return Task.FromException<SubtitleFormatWriteResult>(Failure);
        }
        BeforeReturn?.Invoke();
        return Task.FromResult(Result);
    }
}
