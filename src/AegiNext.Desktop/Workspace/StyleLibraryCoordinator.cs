using System.Collections.Immutable;
using AegiNext.Application.Presets;
using AegiNext.Application.Tasks;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Startup;
using AegiNext.Core.Presets;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;

namespace AegiNext.Desktop.Workspace;

internal sealed class StyleLibraryCoordinator(WorkbenchSession session, IWorkbenchDialogService dialogs)
{
    internal event EventHandler? BusyChanged;
    private readonly Lock operationsGate = new();
    private readonly HashSet<Task> operations = [];
    public Task Completion
    {
        get
        {
            lock (operationsGate)
            {
                return Task.WhenAll(operations);
            }
        }
    }
    private int queuedOperations;
    internal bool IsBusy => queuedOperations > 0 || session.ApplicationContext.StylesBusy;
    internal void Initialize() => Queue(async () =>
    {
        await session.ApplicationContext.Initialization;
        Refresh();
        session.LogInfo("Styles", $"{Localization.Get("WorkflowLog.StyleLibraryLoaded")} ({session.StyleLibrary.Snapshot.Presets.Length})");
    });

    internal void Queue(Func<Task> action)
    {
        queuedOperations++;
        BusyChanged?.Invoke(this, EventArgs.Empty);
        var operation = RunAsync(action);
        lock (operationsGate)
        {
            operations.Add(operation);
        }
        _ = ForgetOperationAsync(operation);
    }

    private async Task ForgetOperationAsync(Task operation)
    {
        await operation.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        lock (operationsGate)
        {
            operations.Remove(operation);
        }
    }

    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            if (!session.IsClosing)
            {
                await session.RunCommandAsync(action);
            }
        }
        finally
        {
            queuedOperations--;
            BusyChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    internal async Task UpsertAsync(SubtitleStylePreset preset)
    {
        await session.ApplicationContext.RunStyleOperationAsync(() => session.StyleLibrary.UpsertAsync(preset));
        Refresh(preset.Id);
        session.LogInfo("Styles", Localization.Get("WorkflowLog.StyleSaved"), preset.Name);
    }

    internal async Task DeleteAsync(Guid id)
    {
        var preset = session.StyleLibrary.Snapshot.Presets.FirstOrDefault(value => value.Id == id);
        await session.ApplicationContext.RunStyleOperationAsync(() => session.StyleLibrary.RemoveAsync(id));
        Refresh();
        if (preset is not null)
        {
            session.LogInfo("Styles", Localization.Get("WorkflowLog.StyleDeleted"), preset.Name);
        }
    }

    internal void Refresh(Guid? selectedId = null)
    {
        if (session.IsClosing)
        {
            return;
        }

        var vm = session.ViewModel.Styles.Subtitle;
        var id = selectedId ?? vm.SelectedPreset?.Id;
        vm.Presets = session.StyleLibrary.Snapshot.Presets.Select(value => new StylePresetListItem(value.Id, value.Name)).ToArray();
        vm.SelectedPreset = vm.Presets.FirstOrDefault(value => value.Id == id) ?? vm.Presets.FirstOrDefault();
        vm.CanApplyPreset = session.HasStyleSelection(LayerKind.SUBTITLE) && !session.IsProjectBusy && vm.Presets.Length > 0;
        session.NotifyStyleLibraryChanged();
    }

    internal async Task CaptureAsync()
    {
        if (session.IsProjectBusy || !session.TryCommitDrafts() || session.SelectedCue is not { } cue)
        {
            return;
        }
        await session.ApplicationContext.Tasks.Submit(new CaptureSubtitleStyleTask(session, this, cue,
            session.Editor.Snapshot, session.ProjectDirectory)).Completion;
    }

    internal async Task CaptureCoreAsync(SubtitleLine cue, ProjectDocument captured, string directory,
        AegiTaskExecutionContext context)
    {
        var index = 1;
        var name = $"{Localization.Get("Workbench.CapturedStyle")} {index}";
        while (session.StyleLibrary.Snapshot.Presets.Any(value => string.Equals(value.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            name = $"{Localization.Get("Workbench.CapturedStyle")} {++index}";
        }
        var preset = await SubtitleStylePresetService.CaptureAsync(name, cue.Style, captured, directory,
            cancellationToken: context.CancellationToken);
        context.EnterCommit();
        await UpsertAsync(preset);
    }

    internal async Task ApplyAsync(SubtitleStylePreset preset)
    {
        if (session.IsClosing || session.IsProjectBusy || !session.HasStyleSelection(LayerKind.SUBTITLE) || !session.TryCommitDrafts())
        {
            return;
        }
        var cueIds = session.StyleSubtitleIds();
        if (cueIds.IsEmpty)
        {
            return;
        }
        await session.ApplicationContext.Tasks.Submit(new ApplySubtitleStyleTask(session, this, preset,
            cueIds, session.Editor.Snapshot, session.TaskInputRevision, session.ProjectDirectory)).Completion;
    }

    internal async Task ApplyCoreAsync(SubtitleStylePreset preset, ImmutableArray<Guid> cueIds, ProjectDocument captured,
        long inputRevision, string directory, AegiTaskExecutionContext context)
    {
        var prepared = await SubtitleStylePresetService.ApplyAsync(preset, captured, directory, cueIds, context.CancellationToken);
        using var editingLease = context.AcquireEditLease();
        context.EnterCommit(() => IsTargetCurrent(captured, inputRevision, directory));
        session.Editor.Apply("Apply subtitle style preset", _ => prepared);
        Refresh(preset.Id);
        session.LogInfo("Styles", Localization.Get("WorkflowLog.StyleApplied"), preset.Name);
        if (session.Controller.Snapshot.State == AegiNext.Media.Playback.VideoPlaybackState.PAUSED)
        {
            await session.Controller.SeekAsync(session.Controller.Snapshot.Position);
        }
    }

    internal async Task ApplyTrackAsync(Guid trackId, Guid presetId)
    {
        if (session.IsProjectBusy || !session.TryCommitDrafts())
        {
            return;
        }
        var captured = session.Editor.Snapshot;
        var inputRevision = session.TaskInputRevision;
        var preset = session.StyleLibrary.Snapshot.Presets.FirstOrDefault(value => value.Id == presetId) ??
            throw new KeyNotFoundException("字幕样式预设不存在。");
        var track = captured.Tracks.FirstOrDefault(value => value.Id == trackId) ??
            throw new KeyNotFoundException("字幕轨道不存在。");
        var count = session.ClipIndex.GetTrackSubtitles(trackId).Length;
        var decision = count == 0 ? TrackStyleUpdateDecision.DEFAULT_ONLY :
            await dialogs.ConfirmTrackStyleChangeAsync(track.Name, preset.Name, count);
        if (decision != TrackStyleUpdateDecision.CANCEL)
        {
            await session.ApplicationContext.Tasks.Submit(new ApplySubtitleTrackStyleTask(session, this, preset,
                trackId, decision, captured, inputRevision, session.ProjectDirectory)).Completion;
        }
    }

    internal async Task ApplyTrackCoreAsync(SubtitleStylePreset preset, Guid trackId, TrackStyleUpdateDecision decision,
        ProjectDocument captured, long inputRevision, string directory, AegiTaskExecutionContext context)
    {
        var prepared = await SubtitleStylePresetService.PrepareAsync(preset, captured, directory, context.CancellationToken);
        using var editingLease = context.AcquireEditLease();
        context.EnterCommit(() => IsTargetCurrent(captured, inputRevision, directory));
        session.Editor.Apply("Apply subtitle track style", _ =>
            AegiNext.Application.ProjectEditingOperations.SetSubtitleTrackStyle(prepared.Project, trackId, preset.Id,
                preset.Name, prepared.Style, decision == TrackStyleUpdateDecision.UPDATE_EXISTING));
        Refresh();
        session.LogInfo("Styles", Localization.Get("Workbench.TrackStyleApplied"), preset.Name);
        if (session.Controller.Snapshot.State == AegiNext.Media.Playback.VideoPlaybackState.PAUSED)
        {
            await session.Controller.SeekAsync(session.Controller.Snapshot.Position);
        }
    }

    private bool IsTargetCurrent(ProjectDocument captured, long inputRevision, string directory) =>
        !session.IsClosing && session.TaskInputRevision == inputRevision && !session.HasProjectDrafts &&
        ReferenceEquals(captured, session.Editor.Snapshot) && directory == session.ProjectDirectory;

    internal Task<PreparedSubtitleStyle> PrepareCreationAsync(Guid? trackId, Guid? fallbackPresetId,
        ProjectDocument? captured = null)
    {
        var project = captured ?? session.Editor.Snapshot;
        var directory = session.ProjectDirectory;
        var preset = session.StyleLibrary.Snapshot.Presets.FirstOrDefault(value => value.Id == fallbackPresetId);
        if (AegiTaskExecutionContext.Current is { } parent)
        {
            return parent.RunStageAsync("Tasks.PrepareSubtitleStyle", context =>
                PrepareCreationCoreAsync(trackId, preset, project, directory, context.CancellationToken),
                [AegiTaskResource.Project(session.TaskScope), AegiTaskResource.DeferredStoragePath(directory),
                    session.ApplicationContext.GetLibraryResource(PersonalLibraryKind.STYLE)]);
        }
        return session.ApplicationContext.Tasks.Submit(new PrepareSubtitleStyleTask(session, trackId,
            preset, project, directory)).Completion;
    }

    internal static Task<PreparedSubtitleStyle> PrepareCreationCoreAsync(Guid? trackId, SubtitleStylePreset? preset,
        ProjectDocument project, string directory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var track = trackId is { } id ? project.Tracks.FirstOrDefault(value => value.Id == id) ??
            throw new KeyNotFoundException("字幕轨道不存在。") : null;
        if (track is { AutoApplyStyle: true, DefaultStyle: { } defaultStyle })
        {
            return Task.FromResult(new PreparedSubtitleStyle(project, defaultStyle, track.StylePresetName!, track.StylePresetId));
        }
        return preset is null ? Task.FromResult(new PreparedSubtitleStyle(project, new())) :
            SubtitleStylePresetService.PrepareAsync(preset, project, directory, cancellationToken);
    }

    internal async Task ImportAsync()
    {
        var path = await dialogs.OpenFileAsync("ImportStyles", "StyleFiles", ["*.aegistyles"]);
        if (path is not null)
        {
            await session.ApplicationContext.Tasks.Submit(new ImportStylePresetsTask(session, path)).Completion;
            Refresh();
            session.LogInfo("Styles", Localization.Get("WorkflowLog.StylesImported"), path);
        }
    }

    internal async Task ExportAsync()
    {
        var path = await dialogs.SaveFileAsync("ExportStyles", "StyleFiles", ["*.aegistyles"], ".aegistyles", "styles.aegistyles");
        if (path is not null)
        {
            await session.ApplicationContext.Tasks.Submit(new ExportStylePresetsTask(session, path, session.StyleLibrary.Snapshot)).Completion;
            session.LogInfo("Styles", Localization.Get("WorkflowLog.StylesExported"), path);
        }
    }
}
