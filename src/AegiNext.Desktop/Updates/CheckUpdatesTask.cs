using AegiNext.Application.Tasks;

namespace AegiNext.Desktop.Updates;

internal sealed class CheckUpdatesTask(IUpdateReleaseSource source, UpdateChannel channel, string currentVersion,
    ReleaseIdentity? currentIdentity = null)
    : AegiTask<UpdateCheckResult>
{
    public override string Name => "Tasks.CheckUpdates";

    /// <inheritdoc />
    protected override Task<UpdateCheckResult> ExecuteResultAsync(AegiTaskExecutionContext context)
    {
        return Task.Run(async () =>
        {
            var localIdentity = currentIdentity;
            if (localIdentity is null && !ReleaseIdentity.TryParse(currentVersion, out localIdentity))
            {
                throw new InvalidDataException("The application does not have a supported release identity.");
            }
            var release = await source.GetLatestAsync(channel, context.CancellationToken).ConfigureAwait(false);
            context.CancellationToken.ThrowIfCancellationRequested();
            var status = release is null ? UpdateCheckStatus.NO_RELEASE
                : release.Identity.CompareTo(localIdentity) > 0 ? UpdateCheckStatus.UPDATE_AVAILABLE : UpdateCheckStatus.UP_TO_DATE;
            return new UpdateCheckResult(status, currentVersion, release);
        }, context.CancellationToken);
    }
}
