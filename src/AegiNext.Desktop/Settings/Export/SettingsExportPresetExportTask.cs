using System.Collections.Immutable;
using AegiNext.Application.Tasks;
using AegiNext.Desktop.Startup;
using AegiNext.Export.Presets;

namespace AegiNext.Desktop.Settings.Export;

internal sealed class SettingsExportPresetExportTask(DesktopApplicationContext owner,
    ImmutableArray<VideoExportPreset> presets, string destination) : AegiTask
{
    public override string Name => "Tasks.ExportPresets";
    public override bool CanCancel => false;
    public override IReadOnlyCollection<AegiTaskResource> Resources =>
        [owner.GetLibraryResource(PersonalLibraryKind.EXPORT), AegiTaskResource.DeferredStoragePath(destination)];

    protected override Task ExecuteAsync(AegiTaskExecutionContext context)
    {
        context.EnterCommit();
        return owner.RunExportPresetOperationAsync(() =>
            owner.ExportPresetLibrary.ExportPresetsAsync(presets, destination, CancellationToken.None));
    }
}
