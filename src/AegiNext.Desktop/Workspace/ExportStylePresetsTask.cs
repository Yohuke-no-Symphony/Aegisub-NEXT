using AegiNext.Application.Presets;
using AegiNext.Application.Tasks;
using AegiNext.Core.Presets;
using AegiNext.Desktop.Startup;

namespace AegiNext.Desktop.Workspace;

internal sealed class ExportStylePresetsTask(WorkbenchSession session, string path, SubtitleStylePresetCollection captured) : AegiTask
{
    public override string Name => "Tasks.ExportStyles";
    public override string ScopeId => session.TaskScope;
    public override string ScopeDisplayName => session.ProjectDisplayName;
    public override IReadOnlyCollection<AegiTaskResource> Resources => [AegiTaskResource.DeferredStoragePath(path)];
    protected override async Task ExecuteAsync(AegiTaskExecutionContext context)
    {
        if (AegiTaskResource.StoragePath(path) == session.ApplicationContext.GetCanonicalLibraryResource(PersonalLibraryKind.STYLE))
        {
            throw new InvalidDataException("导出位置不能是正在使用的样式库文件。");
        }
        session.ApplicationContext.LibraryPaths.RequireExternalExportPath(path);
        await session.ApplicationContext.EffectLibraryTransfer.ExportStylesAsync(captured, path,
            () => context.EnterCommit(), context.CancellationToken);
    }
}
