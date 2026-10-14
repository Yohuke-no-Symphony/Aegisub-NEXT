using System.Collections.Immutable;
using AegiNext.Application.Presets;
using AegiNext.Application.Tasks;
using AegiNext.Core.Presets;
using AegiNext.Desktop.Startup;

namespace AegiNext.Desktop.EffectLibrary.Transfer;

internal sealed class EffectLibraryExportTask : AegiTask
{
    private readonly DesktopApplicationContext owner;
    private readonly ImmutableArray<SubtitleStylePreset> styles;
    private readonly ImmutableArray<EffectScriptPreset> effects;
    private readonly string destination;
    private readonly EffectLibraryExportMode mode;
    private readonly IReadOnlyList<string> paths;

    internal EffectLibraryExportTask(DesktopApplicationContext owner, ImmutableArray<SubtitleStylePreset> styles,
        ImmutableArray<EffectScriptPreset> effects, string destination, EffectLibraryExportMode mode)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        if (styles.IsDefault || effects.IsDefault || !Enum.IsDefined(mode))
        {
            throw new ArgumentException("导出选择或格式无效。");
        }
        this.owner = owner;
        this.styles = styles;
        this.effects = effects;
        this.destination = Path.GetFullPath(destination);
        this.mode = mode;
        paths = mode switch
        {
            EffectLibraryExportMode.INDEPENDENT_FILES => owner.EffectLibraryTransfer.GetIndependentDestinations(styles, effects, this.destination),
            EffectLibraryExportMode.SCRIPT_SOURCE_FILES when styles.IsEmpty => PresetBatchExporter.GetEffectDestinations(effects, this.destination),
            EffectLibraryExportMode.PACKAGE => [this.destination],
            EffectLibraryExportMode.SINGLE_FILE when styles.Length + effects.Length == 1 => [this.destination],
            EffectLibraryExportMode.SCRIPT_SOURCE when styles.IsEmpty && effects.Length == 1 => [this.destination],
            _ => throw new ArgumentException("此导出格式不适用于当前选择。", nameof(mode))
        };
    }

    public override string Name => "Tasks.ExportEffectLibrary";
    public override IReadOnlyCollection<AegiTaskResource> Resources =>
        [.. paths.Select(AegiTaskResource.DeferredStoragePath)];

    protected override Task ExecuteAsync(AegiTaskExecutionContext context)
    {
        foreach (var path in paths)
        {
            owner.LibraryPaths.RequireExternalExportPath(path);
        }
        if (mode is not (EffectLibraryExportMode.SCRIPT_SOURCE or EffectLibraryExportMode.SCRIPT_SOURCE_FILES) &&
            owner.EffectLibraryLoadError is { } error)
        {
            throw new InvalidOperationException("效果库尚未完整加载，无法导出完整素材。", error);
        }
        context.ReportProgress(new(Name));
        var token = context.CancellationToken;
        Action beforeCommit = () => context.EnterCommit();
        return Task.Run(() => mode switch
        {
            EffectLibraryExportMode.PACKAGE => owner.EffectLibraryTransfer.ExportPackageAsync(styles, effects, destination, beforeCommit, token),
            EffectLibraryExportMode.INDEPENDENT_FILES => owner.EffectLibraryTransfer.ExportIndependentAsync(styles, effects, destination, beforeCommit, token),
            EffectLibraryExportMode.SCRIPT_SOURCE_FILES => PresetBatchExporter.ExportEffectsAsync(effects, destination, beforeCommit, token),
            EffectLibraryExportMode.SCRIPT_SOURCE => owner.EffectLibraryTransfer.ExportEffectSourceAsync(effects.Single(), destination, beforeCommit, token),
            EffectLibraryExportMode.SINGLE_FILE when !styles.IsEmpty => owner.EffectLibraryTransfer.ExportStyleAsync(styles.Single(), destination, beforeCommit, token),
            EffectLibraryExportMode.SINGLE_FILE => owner.EffectLibraryTransfer.ExportEffectAsync(effects.Single(), destination, beforeCommit, token),
            _ => throw new InvalidOperationException("导出格式无效。")
        }, token);
    }
}
