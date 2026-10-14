using System.Collections.Immutable;
using AegiNext.Application.Presets;
using AegiNext.Application.Tasks;
using AegiNext.Core.Presets;
using AegiNext.Desktop.Startup;

namespace AegiNext.Desktop.Settings.Presets;

internal sealed class SettingsStyleExportTask(DesktopApplicationContext owner, ImmutableArray<SubtitleStylePreset> selection,
    string destination, bool batch) : AegiTask
{
    private readonly IReadOnlyList<string> paths = batch
        ? PresetBatchExporter.GetStyleDestinations(selection, destination)
        : [Path.GetFullPath(destination)];

    public override string Name => "Tasks.ExportStyles";

    public override IReadOnlyCollection<AegiTaskResource> Resources =>
        [.. paths.Select(AegiTaskResource.DeferredStoragePath)];

    protected override Task ExecuteAsync(AegiTaskExecutionContext context)
    {
        foreach (var path in paths)
        {
            owner.LibraryPaths.RequireExternalExportPath(path);
        }

        context.ReportProgress(new(Name));
        return Task.Run(() => batch
            ? PresetBatchExporter.ExportStylesAsync(selection, destination, () => context.EnterCommit(), context.CancellationToken)
            : SubtitleStylePresetStore.SaveAsync(new() { Presets = selection }, destination,
                () => context.EnterCommit(), context.CancellationToken), context.CancellationToken);
    }
}
