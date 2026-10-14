using AegiNext.Application.Tasks;

namespace AegiNext.Desktop.Startup;

internal sealed class PersonalLibraryTask(DesktopApplicationContext owner, PersonalLibraryKind kind,
    Func<Task> operation, bool propagateFailure, bool reportError = true) : AegiTask
{
    public override string Name => kind switch
    {
        PersonalLibraryKind.STYLE => "Tasks.StyleLibrary",
        PersonalLibraryKind.EFFECT or PersonalLibraryKind.EFFECT_LIBRARY => "Tasks.EffectLibrary",
        PersonalLibraryKind.EXPORT => "Tasks.ExportPresetLibrary",
        PersonalLibraryKind.COLOR_TAG => "Tasks.ColorTagLibrary",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
    public override bool CanCancel => false;
    public override IReadOnlyCollection<AegiTaskResource> Resources => [owner.GetLibraryResource(kind)];

    protected override Task ExecuteAsync(AegiTaskExecutionContext context)
    {
        return owner.ExecuteLibraryOperationAsync(operation, kind, propagateFailure, reportError);
    }
}
