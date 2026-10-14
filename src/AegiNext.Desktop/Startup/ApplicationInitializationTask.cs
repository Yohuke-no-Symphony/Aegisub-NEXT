using AegiNext.Application.Tasks;

namespace AegiNext.Desktop.Startup;

internal sealed class ApplicationInitializationTask(DesktopApplicationContext owner) : AegiTask
{
    public override string Name => "Tasks.Initialization";
    public override AegiTaskMode Mode => AegiTaskMode.Blocking;
    public override bool CanCancel => false;
    public override IReadOnlyCollection<AegiTaskResource> Resources => owner.InitializationResources;

    protected override Task ExecuteAsync(AegiTaskExecutionContext context)
    {
        return owner.InitializeAsync(context);
    }
}
