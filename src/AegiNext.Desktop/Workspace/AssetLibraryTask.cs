using AegiNext.Application.Tasks;

namespace AegiNext.Desktop.Workspace;

internal sealed class AssetLibraryTask(WorkbenchSession session, Func<AegiTaskExecutionContext, Task> operation)
    : ProjectWorkflowTask<bool>(session)
{
    public override string Name => "Tasks.ImportImages";

    protected override async Task<bool> ExecuteResultAsync(AegiTaskExecutionContext context)
    {
        await operation(context);
        return true;
    }
}
