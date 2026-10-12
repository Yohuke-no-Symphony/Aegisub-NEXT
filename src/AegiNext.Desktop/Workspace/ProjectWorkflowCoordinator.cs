using System.Collections.Immutable;
using System.Text;
using AegiNext.Application.Tasks;
using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controllers;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;
using AegiNext.Rendering.SubtitleFormats;
using AegiNext.SubtitleFormats;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class ProjectWorkflowCoordinator(WorkbenchSession session, IWorkbenchDialogService dialogs)
{
    private UnavailableProjectMediaBinding? unavailableMediaBinding;
    internal bool IsNewProjectDialogOpen { get; private set; }
    internal async Task<bool> ConfirmDiscardOrSaveAsync()
    {
        if (!session.TryCommitDrafts())
        {
            return false;
        }

        if (!session.HasUnsavedChanges)
        {
            return true;
        }

        var result = await dialogs.ConfirmUnsavedAsync();
        return result == 2 || result == 1 && await SaveProjectAsync(false);
    }

    internal async Task NewProjectAsync()
    {
        if (session.IsProjectBusy || session.IsClosing || IsNewProjectDialogOpen)
        {
            return;
        }
        IsNewProjectDialogOpen = true;
        session.ViewModel.RefreshCommands();
        try
        {
            await using var pause = await session.Persistence.PauseAsync();
            if (!await ConfirmDiscardOrSaveAsync() || session.IsClosing)
            {
                return;
            }
            await dialogs.ShowNewProjectAsync(session.Preferences.Projects.WorkspaceRoot, async (request, cancellationToken) =>
            {
                var result = await CreateProjectFromDialogAsync(request, cancellationToken);
                foreach (var diagnostic in result.Diagnostics)
                {
                    session.ShowError(diagnostic);
                }
                return result;
            }, session.ProjectOperationsToken);
        }
        finally
        {
            IsNewProjectDialogOpen = false;
            session.ViewModel.RefreshCommands();
        }
    }

    internal Task<ProjectOpenResult> CreateProjectAsync(ProjectCreationRequest request,
        CancellationToken cancellationToken = default)
    {
        return SubmitProjectCreationAsync(null, request, ownsCancellation: false, cancellationToken);
    }

    internal Task<ProjectOpenResult> CreateProjectAsync(string path,
        CancellationToken cancellationToken = default)
    {
        return SubmitProjectCreationAsync(path, null, ownsCancellation: false, cancellationToken);
    }

    internal Task<ProjectOpenResult> CreateProjectFromDialogAsync(ProjectCreationRequest request,
        CancellationToken operationCancellation)
    {
        return SubmitProjectCreationAsync(null, request, ownsCancellation: true, operationCancellation);
    }

    private async Task<ProjectOpenResult> SubmitProjectCreationAsync(string? path, ProjectCreationRequest? request,
        bool ownsCancellation, CancellationToken cancellationToken)
    {
        if (session.IsProjectBusy || session.IsClosing || cancellationToken.IsCancellationRequested)
        {
            return new(ProjectOpenStatus.CANCELLED);
        }
        await using var pause = await session.Persistence.PauseAsync();
        var task = new CreateProjectTask(session, this, path ?? ProjectCreationService.GetProjectPath(request!), request);
        var handle = session.ApplicationContext.Tasks.Submit(task);
        if (ownsCancellation)
        {
            using var registration = cancellationToken.Register(() => handle.RequestCancel());
            return await AwaitProjectResultAsync(handle, CancellationToken.None, () => task.Outcome);
        }
        return await AwaitProjectResultAsync(handle, cancellationToken, () => task.Outcome);
    }

    private static async Task<ProjectOpenResult> AwaitProjectResultAsync(AegiTaskHandle<ProjectOpenResult> handle,
        CancellationToken cancellationToken, Func<ProjectOpenResult?>? outcome = null)
    {
        try
        {
            return await handle.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return new(ProjectOpenStatus.CANCELLED);
        }
        catch (Exception error)
        {
            if (outcome?.Invoke() is { Status: ProjectOpenStatus.OPENED } committed)
            {
                return new(ProjectOpenStatus.OPENED, diagnostics: [.. committed.Diagnostics, error]);
            }
            return new(ProjectOpenStatus.FAILED, error);
        }
    }

    internal async Task<ProjectOpenResult> CreateProjectCoreAsync(string path, ProjectCreationRequest? request,
        AegiTaskExecutionContext context)
    {
        var cancellationToken = context.CancellationToken;
        if (session.IsProjectBusy || session.IsClosing || cancellationToken.IsCancellationRequested)
        {
            return new(ProjectOpenStatus.CANCELLED);
        }

        var previousDocument = session.Editor.Snapshot;
        var previousPreview = session.Controller.Snapshot;
        var previewChanged = false;
        var committed = false;
        using var editLease = context.AcquireEditLease();
        try
        {
            path = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(path)!;
            if (request is not null)
            {
                ProjectCreationService.Validate(request);
            }
            previewChanged = true;
            await session.Controller.CloseMediaAsync();
            cancellationToken.ThrowIfCancellationRequested();
            if (session.IsClosing)
            {
                throw new OperationCanceledException();
            }

            ProjectDocument document;
            if (request is not null)
            {
                var created = await ProjectCreationService.CreateAsync(request, cancellationToken);
                path = created.Path;
                document = created.Document;
            }
            else
            {
                document = ProjectCreationService.CreateDocument(Path.GetFileNameWithoutExtension(path));
                await ProjectStore.CreateAsync(document, path, cancellationToken);
            }
            context.EnterCommit(() => !session.IsClosing);
            session.SetProjectLocation(path, directory);
            session.ClearTimelineClipboard();
            session.ResetSelection();
            session.ResetTimelineViewState(document);
            session.Editor.Reset(document);
            session.ActivateProjectPersistence();
            unavailableMediaBinding = null;
            committed = true;
            await session.ApplicationContext.RecentProjects.RecordAsync(path);
            var diagnostics = new List<Exception>();
            try
            {
                await session.Analysis.ClearAsync();
            }
            catch (Exception error)
            {
                diagnostics.Add(error);
            }

            session.LogInfo("Project", Localization.Get("WorkflowLog.ProjectCreated"), path);
            return new(ProjectOpenStatus.OPENED, diagnostics: diagnostics.AsReadOnly());
        }
        catch (Exception error)
        {
            if (committed)
            {
                return new(ProjectOpenStatus.OPENED, diagnostics: [error]);
            }

            if (previewChanged)
            {
                try
                {
                    await RestorePreviewAsync(previousDocument, previousPreview, error);
                }
                catch (Exception recoveryError)
                {
                    return new(ProjectOpenStatus.FAILED, recoveryError);
                }
            }
            return error is OperationCanceledException
                ? new(ProjectOpenStatus.CANCELLED)
                : new(ProjectOpenStatus.FAILED, error);
        }

    }

    internal async Task OpenProjectAsync()
    {
        if (session.IsProjectBusy)
        {
            return;
        }

        await using var pickerPause = await session.Persistence.PauseAsync();
        var path = await dialogs.OpenFileAsync("OpenProject", "Projects", ["*.aeginext"]);
        if (path is null)
        {
            return;
        }

        var result = await OpenProjectAsync(path);
        if (result.Error is { } error)
        {
            session.ShowError(error);
        }

        foreach (var diagnostic in result.Diagnostics)
        {
            session.ShowError(diagnostic);
        }
    }

    internal async Task<ProjectOpenResult> OpenProjectAsync(string path,
        CancellationToken cancellationToken = default)
    {
        if (session.IsProjectBusy || session.IsClosing || cancellationToken.IsCancellationRequested)
        {
            return new(ProjectOpenStatus.CANCELLED);
        }

        await using var pause = await session.Persistence.PauseAsync();
        try
        {
            path = Path.GetFullPath(path);
            if (ProjectBackupStore.IsBackupPath(path))
            {
                return new(ProjectOpenStatus.FAILED, new InvalidDataException(Localization.Get("Workbench.BackupRestoreRequired")));
            }
            if (!await ConfirmDiscardOrSaveAsync() || session.IsProjectBusy || session.IsClosing ||
                cancellationToken.IsCancellationRequested)
            {
                return new(ProjectOpenStatus.CANCELLED);
            }
        }
        catch (OperationCanceledException)
        {
            return new(ProjectOpenStatus.CANCELLED);
        }
        catch (Exception error)
        {
            return new(ProjectOpenStatus.FAILED, error);
        }

        var task = new OpenProjectTask(session, this, path);
        return await AwaitProjectResultAsync(session.ApplicationContext.Tasks.Submit(task), cancellationToken, () => task.Outcome);
    }

    internal async Task<ProjectOpenResult> OpenProjectCoreAsync(string path, AegiTaskExecutionContext context)
    {
        var cancellationToken = context.CancellationToken;
        if (session.IsProjectBusy || session.IsClosing || cancellationToken.IsCancellationRequested)
        {
            return new(ProjectOpenStatus.CANCELLED);
        }

        var previousDocument = session.Editor.Snapshot;
        var inputRevision = session.TaskInputRevision;
        var previousPreview = session.Controller.Snapshot;
        var previewChanged = false;
        Exception? unavailableMediaError = null;
        var committed = false;
        AegiTaskEditLease? editLease = null;
        try
        {
            var document = await ProjectStore.LoadAsync(path, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (inputRevision != session.TaskInputRevision || session.HasProjectDrafts ||
                !ReferenceEquals(previousDocument, session.Editor.Snapshot))
            {
                throw new OperationCanceledException("The project input changed during preparation.", cancellationToken);
            }
            editLease = context.AcquireEditLease();
            var directory = Path.GetDirectoryName(path)!;
            string? mediaPath = null;
            UnavailableProjectMediaBinding? deferredBinding = null;
            if (document.Media is not { } binding)
            {
                previewChanged = true;
                await session.Controller.CloseMediaAsync();
            }
            else
            {
                var asset = document.Assets.Single(asset => asset.Id == binding.AssetId);
                try
                {
                    mediaPath = ProjectAssetLocation.Resolve(asset, directory);
                    previewChanged = true;
                    await session.Controller.OpenAsync(mediaPath, cancellationToken);
                    if (session.Controller.Snapshot.Error is { } error)
                    {
                        throw error;
                    }

                    if (session.Controller.MediaInfo is null)
                    {
                        throw new InvalidDataException("媒体信息不可用。");
                    }
                }
                catch (Exception error) when (error is not OperationCanceledException &&
                    !cancellationToken.IsCancellationRequested && !session.IsClosing)
                {
                    unavailableMediaError = error;
                    var reference = mediaPath ?? asset.ExternalPath ?? asset.RelativePath;
                    var proceed = await dialogs.ConfirmUnavailableMediaAsync(reference, error.Message, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!proceed || session.IsClosing)
                    {
                        throw new OperationCanceledException();
                    }

                    previewChanged = true;
                    await session.Controller.CloseMediaAsync();
                    mediaPath = null;
                    deferredBinding = new(binding, reference);
                }

                if (deferredBinding is null)
                {
                    ProjectMediaBindingValidator.Validate(document, session.Controller.MediaInfo!);
                    document = WithConfirmedPlaybackOrigin(document, session.Controller.MediaInfo!);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (session.IsClosing)
            {
                throw new OperationCanceledException();
            }

            context.EnterCommit(() => !session.IsClosing);
            session.SetProjectLocation(path, directory);
            session.ClearTimelineClipboard();
            session.ResetSelection();
            session.ResetTimelineViewState(document);
            unavailableMediaBinding = deferredBinding;
            session.Editor.Reset(document);
            session.ActivateProjectPersistence();
            committed = true;
            await session.ApplicationContext.RecentProjects.RecordAsync(path);
            var diagnostics = new List<Exception>();
            if (mediaPath is not null)
            {
                try
                {
                    await session.Controller.SeekAsync(session.Controller.Snapshot.Start ?? MediaTime.Zero);
                    if (session.Controller.Snapshot.Error is { } previewError)
                    {
                        diagnostics.Add(previewError);
                    }
                }
                catch (Exception error)
                {
                    diagnostics.Add(error);
                }

                try
                {
                    await session.Analysis.StartAsync(mediaPath);
                }
                catch (Exception error)
                {
                    diagnostics.Add(error);
                }
            }
            else
            {
                try
                {
                    await session.Analysis.ClearAsync();
                }
                catch (Exception error)
                {
                    diagnostics.Add(error);
                }
            }
            session.LogInfo("Project", Localization.Get("WorkflowLog.ProjectOpened"), path);
            return new(ProjectOpenStatus.OPENED, diagnostics: diagnostics.AsReadOnly());
        }
        catch (Exception error)
        {
            if (!committed)
            {
                if (previewChanged)
                {
                    try
                    {
                        await RestorePreviewAsync(previousDocument, previousPreview, error);
                    }
                    catch (Exception recoveryError)
                    {
                        return new(ProjectOpenStatus.FAILED, recoveryError);
                    }
                }

                return error is OperationCanceledException
                    ? new(ProjectOpenStatus.CANCELLED)
                    : new(ProjectOpenStatus.FAILED, error);
            }

            return new(ProjectOpenStatus.OPENED, diagnostics: [error]);
        }
        finally
        {
            editLease?.Dispose();
            if (unavailableMediaError is { } error && !ReferenceEquals(session.Controller.Snapshot.Error, error))
            {
                session.DismissError(error);
            }

        }
    }

    internal async Task OpenMediaAsync(string path, bool updateProject)
    {
        if (session.IsProjectBusy || !session.TryCommitDrafts())
        {
            return;
        }

        path = ProjectAssetLocation.ResolveInputPath(path, session.ProjectDirectory);
        await session.ApplicationContext.Tasks.Submit(new SwitchProjectMediaTask(session, this, path, updateProject)).Completion;
    }

    internal async Task OpenMediaCoreAsync(string path, bool updateProject, AegiTaskExecutionContext context)
    {
        using var editLease = context.AcquireEditLease();
        var previousDocument = session.Editor.Snapshot;
        var previousPreview = session.Controller.Snapshot;
        var committed = false;
        try
        {
            session.ResetTiming();
            await session.Controller.OpenAsync(path, context.CancellationToken);
            if (session.Controller.Snapshot.Error is { } error)
            {
                throw error;
            }

            var media = session.Controller.MediaInfo ?? throw new InvalidDataException("媒体信息不可用。");
            if (updateProject)
            {
                var asset = await ProjectResources.ImportAsync(path, ProjectAssetKind.MEDIA, session.ProjectDirectory, context.CancellationToken);
                context.EnterCommit(() => !session.IsClosing);
                session.Editor.Apply("Bind media", document => document with
                {
                    Width = media.VideoWidth ?? document.Width, Height = media.VideoHeight ?? document.Height,
                    FrameRate = media.FrameRate ?? document.FrameRate,
                    Assets = document.Assets.Where(value => value.Kind != ProjectAssetKind.MEDIA).Append(asset)
                        .ToImmutableArray(),
                    Media = new(asset.Id, media.VideoStreamIndex, media.AudioStreamIndex, media.Start ?? MediaTime.Zero)
                    {
                        PlaybackOrigin = media.PlaybackOrigin
                    }
                });
                committed = true;
                await session.Controller.SeekAsync(media.Start ?? MediaTime.Zero);
            }

            await session.Analysis.StartAsync(path);
            session.Tick();
            session.ViewModel.Timeline.AdaptViewportToMedia();
            session.LogInfo("Media", Localization.Get("WorkflowLog.MediaOpened"), path);
        }
        catch (Exception error)
        {
            if (!committed)
            {
                await RestorePreviewAsync(previousDocument, previousPreview, error);
            }

            throw;
        }

    }

    private async Task RestorePreviewAsync(ProjectDocument document, VideoPreviewSnapshot previousPreview,
        Exception originalError)
    {
        try
        {
            if (previousPreview.FilePath is not { } path)
            {
                await session.Controller.CloseMediaAsync();
                return;
            }

            await session.Controller.OpenAsync(path);
            if (session.Controller.Snapshot.Error is { } error)
            {
                throw error;
            }

            if (document.Media is not null)
            {
                ProjectMediaBindingValidator.Validate(document,
                    session.Controller.MediaInfo ?? throw new InvalidDataException("媒体信息不可用。"));
            }
            await session.Controller.SeekAsync(previousPreview.Position);
        }
        catch (Exception recoveryError)
        {
            try
            {
                await ClearPreviewBindingAsync();
            }
            catch (Exception closeError)
            {
                throw new AggregateException(originalError, recoveryError, closeError);
            }

            throw new AggregateException(originalError, recoveryError);
        }
    }

    internal async Task<bool> SaveProjectAsync(bool saveAs)
    {
        if (session.IsProjectBusy || session.IsClosing)
        {
            return false;
        }
        await using var pause = await session.Persistence.PauseAsync();
        if (!session.TryCommitDrafts())
        {
            return false;
        }

        var destination = session.ProjectPath;
        if (saveAs || destination is null)
        {
            destination = await dialogs.SaveFileAsync("Save", "Projects", ["*.aeginext"], ".aeginext",
                session.ProjectDisplayName + ".aeginext");
            if (destination is null)
            {
                return false;
            }
        }

        if (session.IsClosing)
        {
            return false;
        }
        var captured = session.Editor.Snapshot;
        var snapshot = WithCurrentPlaybackOrigin(session.CreatePersistenceSnapshot(captured));
        return await session.ApplicationContext.Tasks.Submit(new SaveProjectTask(session, this, destination, captured,
            snapshot, session.ProjectDirectory)).Completion;
    }

    internal async Task<bool> SaveProjectCoreAsync(string destination, ProjectDocument snapshot,
        ProjectDocument persistenceSnapshot, string sourceDirectory, AegiTaskExecutionContext context, bool finalization = false)
    {
        destination = Path.GetFullPath(destination);
        var locationChanged = !WorkbenchSession.PathsEqual(destination, session.ProjectPath);
        var directory = Path.GetDirectoryName(destination)!;
        var sameDirectory = WorkbenchSession.PathsEqual(directory, sourceDirectory);
        using var relocationLease = sameDirectory ? null : context.AcquireEditLease();
        var expectedCurrent = session.Editor.Snapshot;
        await using var savedResources = sameDirectory ? null
            : await ProjectResources.PrepareRebaseAsync(persistenceSnapshot, sourceDirectory, directory, context.CancellationToken);
        var prepared = sameDirectory
            ? ProjectResources.NormalizeMediaReferences(persistenceSnapshot, directory)
            : savedResources!.Snapshot;
        await using var currentResources = sameDirectory || ReferenceEquals(snapshot, expectedCurrent) ? null
            : await ProjectResources.PrepareRebaseAsync(session.CreatePersistenceSnapshot(expectedCurrent),
                sourceDirectory, directory, context.CancellationToken);
        var relocatedCurrent = currentResources?.Snapshot ?? prepared;
        if (savedResources is not null)
        {
            await savedResources.CommitAsync(context.CancellationToken);
        }
        if (currentResources is not null)
        {
            await currentResources.CommitAsync(context.CancellationToken);
        }
        await ProjectStore.SaveAsync(prepared, destination, () => context.EnterCommit(() =>
            (finalization || !session.IsClosing) && session.ProjectDirectory == sourceDirectory &&
            (sameDirectory || ReferenceEquals(expectedCurrent, session.Editor.Snapshot))), context.CancellationToken);
        savedResources?.Accept();
        currentResources?.Accept();
        session.SetProjectLocation(destination, directory);
        session.Analysis.ProjectDirectoryChanged(directory);
        if (sameDirectory)
        {
            session.AcceptProjectSave(snapshot, prepared);
        }
        else
        {
            session.AcceptRelocatedProjectSave(expectedCurrent, relocatedCurrent, snapshot, prepared);
        }
        if (locationChanged)
        {
            session.ActivateProjectPersistence();
        }
        await session.Persistence.RecordManualSaveAsync(prepared);
        session.LogInfo("Project", Localization.Get("Workbench.Saved"), destination);
        await session.ApplicationContext.RecentProjects.RecordAsync(destination);
        return true;
    }

    internal async Task<bool> SaveProjectForCloseAsync(AegiTaskScopeCloseLease lease, string? destination = null)
    {
        destination ??= session.ProjectPath;
        if (destination is null)
        {
            destination = await dialogs.SaveFileAsync("Save", "Projects", ["*.aeginext"], ".aeginext",
                session.ProjectDisplayName + ".aeginext");
            if (destination is null)
            {
                return false;
            }
        }
        var snapshot = session.Editor.Snapshot;
        return await lease.SubmitFinalization(new SaveProjectTask(session, this, destination, snapshot,
            WithCurrentPlaybackOrigin(session.CreatePersistenceSnapshot(snapshot)), session.ProjectDirectory, finalization: true)).Completion;
    }

    internal async Task ImportSubtitlesAsync(bool ass = false)
    {
        if (session.IsProjectBusy || !session.TryCommitDrafts())
        {
            return;
        }
        var path = await dialogs.OpenFileAsync("Import", "SubtitleFiles", ass ? ["*.ass"] : ["*.srt"]);
        if (path is null || session.IsClosing)
        {
            return;
        }
        var captured = session.Editor.Snapshot;
        var trackId = session.CurrentTrackId;
        var presetId = session.ViewModel.Styles.Subtitle.SelectedPreset?.Id;
        var fontLoading = ass ? session.Fonts.EnsureLoadedAsync() : Task.CompletedTask;
        await session.ApplicationContext.Tasks.Submit(new ImportSubtitlesTask(session, this, path, ass,
            captured, session.TaskInputRevision, trackId, presetId, fontLoading)).Completion;
    }

    internal async Task ImportSubtitlesCoreAsync(string path, bool ass, ProjectDocument captured, long inputRevision,
        Guid? trackId, Guid? presetId, Task fontLoading, AegiTaskExecutionContext context)
    {
        Guid? firstCueId;
        var text = await ReadSubtitleFileAsync(path, context.CancellationToken);
        var prepared = WithCurrentPlaybackOrigin(captured);
        var mapping = SubtitleTimelineExchange.GetMapping(prepared.Media);
        var timingDiagnostics = SubtitleTimingDiagnostics(mapping);
        ImmutableArray<SubtitleLine> lines;
        AssImportResult? assResult = null;
        if (ass)
        {
            var fontWeightResolver = await CaptureAssFontWeightResolverAsync(fontLoading, context.CancellationToken);
            var result = await Task.Run(() => AssSubtitleFormat.Parse(text, captured.Width, captured.Height, fontWeightResolver),
                context.CancellationToken);
            if (!await ConfirmConversionAsync(result.Diagnostics.AddRange(timingDiagnostics), result.Lines))
            {
                return;
            }
            assResult = SubtitleTimelineExchange.ToProjectTime(result, mapping ?? default);
            lines = assResult.Lines;
        }
        else
        {
            lines = await Task.Run(() => SubtitleTextFormat.ParseSrt(text), context.CancellationToken);
            if (!await ConfirmConversionAsync(timingDiagnostics, lines))
            {
                return;
            }
            lines = SubtitleTimelineExchange.ToProjectTime(lines, mapping ?? default);
            var creation = await session.Styles.PrepareCreationAsync(trackId, presetId, captured);
            prepared = WithCurrentPlaybackOrigin(creation.Project);
            lines = lines.Select(line => line with
            {
                Style = creation.Style, StyleName = creation.StyleName, StylePresetId = creation.StylePresetId
            }).ToImmutableArray();
        }
        if (session.IsClosing || inputRevision != session.TaskInputRevision || session.HasProjectDrafts ||
            !ReferenceEquals(captured, session.Editor.Snapshot))
        {
            throw new OperationCanceledException("The subtitle import target changed during preparation.", context.CancellationToken);
        }
        var imported = assResult is null
            ? ProjectEditingOperations.ImportSubtitleLines(prepared, lines, Path.GetFileNameWithoutExtension(path))
            : ProjectEditingOperations.ImportSubtitleLines(prepared, assResult, Path.GetFileNameWithoutExtension(path));
        using var editLease = context.AcquireEditLease();
        context.EnterCommit(() => !session.IsClosing && inputRevision == session.TaskInputRevision &&
            !session.HasProjectDrafts && ReferenceEquals(captured, session.Editor.Snapshot));
        session.Editor.Apply("Import subtitles", _ => imported);
        firstCueId = lines.IsEmpty ? null : lines[0].Id;
        session.LogInfo("Subtitles", $"{Localization.Get("WorkflowLog.SubtitlesImported")} ({lines.Length})", path);
        editLease.Dispose();

        if (firstCueId is { } id)
        {
            session.SelectTrack(session.ClipIndex.GetSubtitleTrackId(id));
            session.SelectCue(id);
        }
    }

    private async Task<AssFontWeightResolver> CaptureAssFontWeightResolverAsync(Task fontLoading, CancellationToken cancellationToken)
    {
        try
        {
            await fontLoading.WaitAsync(cancellationToken);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new([]);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(session.Fonts.Faces);
    }

    internal async Task ExportSubtitlesAsync(bool ass = false)
    {
        if (session.IsProjectBusy || !session.TryCommitDrafts())
        {
            return;
        }
        var document = WithCurrentPlaybackOrigin(session.Editor.Snapshot);
        var extension = ass ? ".ass" : ".srt";
        var path = await dialogs.SaveFileAsync("ExportText", "SubtitleFiles", ["*" + extension], extension, "subtitles" + extension);
        if (path is not null && !session.IsClosing)
        {
            await session.ApplicationContext.Tasks.Submit(new ExportSubtitlesTask(session, this, path, document, ass)).Completion;
        }
    }

    internal async Task ExportSubtitlesCoreAsync(string path, ProjectDocument document, bool ass,
        AegiTaskExecutionContext context)
    {
        var mapping = SubtitleTimelineExchange.GetMapping(document.Media);
        var timeOffset = mapping?.Origin ?? MediaTime.Zero;
        var projectDirectory = session.ProjectDirectory;
        var result = await Task.Run(() =>
        {
            if (ass)
            {
                using var measurer = new AssSubtitlePlacementMeasurer(projectDirectory);
                return AssSubtitleFormat.Write(document, timeOffset, measurer);
            }
            return new SubtitleFormatWriteResult(
                SubtitleTextFormat.WriteSrt(document.Subtitles.OrderBy(line => line.Start), timeOffset),
                SubtitleFormatLossAnalysis.ForSrt(document));
        }, context.CancellationToken);
        if (!await ConfirmConversionAsync(result.Diagnostics.AddRange(SubtitleTimingDiagnostics(mapping)), document.Subtitles, document.Layers))
        {
            throw new OperationCanceledException(context.CancellationToken);
        }
        context.CancellationToken.ThrowIfCancellationRequested();
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, result.Text, new UTF8Encoding(false, true), context.CancellationToken);
            context.EnterCommit();
            File.Move(temporary, path, true);
            session.LogInfo("Subtitles", Localization.Get("WorkflowLog.SubtitlesExported"), path);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static ProjectDocument WithConfirmedPlaybackOrigin(ProjectDocument document, VideoPreviewMedia media)
    {
        return document.Media is { PlaybackOrigin: null } binding && media.PlaybackOrigin is { } origin &&
            binding.VideoStreamIndex == media.VideoStreamIndex && binding.AudioStreamIndex == media.AudioStreamIndex &&
            binding.MediaOrigin == (media.Start ?? MediaTime.Zero)
            ? document with { Media = binding with { PlaybackOrigin = origin } } : document;
    }

    private ProjectDocument WithCurrentPlaybackOrigin(ProjectDocument document)
    {
        return IsPreviewBindingSynchronized() && session.Controller.MediaInfo is { } media
            ? WithConfirmedPlaybackOrigin(document, media) : document;
    }

    private static ImmutableArray<SubtitleFormatDiagnostic> SubtitleTimingDiagnostics(MediaTimelineMapping? mapping)
    {
        return mapping.HasValue ? [] : [new("Subtitle.PlaybackOriginUnknown", Localization.Get("Workflow.SubtitleTimelineOriginUnknown"))];
    }

    private Task<bool> ConfirmConversionAsync(ImmutableArray<SubtitleFormatDiagnostic> diagnostics,
        ImmutableArray<SubtitleLine> lines, ImmutableArray<ProjectLayer> layers = default)
    {
        if (diagnostics.IsEmpty)
        {
            return Task.FromResult(true);
        }
        return dialogs.ConfirmSubtitleConversionAsync(new(diagnostics, lines, layers));
    }

    private static async Task<string> ReadSubtitleFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true),
            detectEncodingFromByteOrderMarks: false, bufferSize: 8192, leaveOpen: true);
        var text = await reader.ReadToEndAsync(cancellationToken);
        return text.StartsWith('\uFEFF') ? text[1..] : text;
    }

    internal bool IsPreviewBindingSynchronized()
    {
        var document = session.Editor.Snapshot;
        var path = document.Media is { } binding &&
            unavailableMediaBinding?.Matches(document, session.ProjectDirectory) != true
            ? ProjectAssetLocation.Resolve(document.Assets.Single(asset => asset.Id == binding.AssetId), session.ProjectDirectory)
            : null;
        return string.Equals(path, session.Controller.Snapshot.FilePath, OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    internal Task SynchronizePreviewBindingAsync()
    {
        if (AegiTaskExecutionContext.Current is { } parent)
        {
            return parent.RunStageAsync("Tasks.SynchronizeMedia", SynchronizePreviewBindingCoreAsync,
                [AegiTaskResource.Project(session.TaskScope), AegiTaskResource.Named("media:" + session.TaskScope)]);
        }
        return session.ApplicationContext.Tasks.Submit(new SynchronizeProjectMediaTask(session, this)).Completion;
    }

    internal async Task SynchronizePreviewBindingCoreAsync(AegiTaskExecutionContext context)
    {
        using var editLease = context.AcquireEditLease();
        try
        {
            var document = session.Editor.Snapshot;
            if (document.Media is not { } binding ||
                unavailableMediaBinding?.Matches(document, session.ProjectDirectory) == true)
            {
                await session.Controller.CloseMediaAsync();
                await session.Analysis.ClearAsync();


            }
            else
            {
                var path = ProjectAssetLocation.Resolve(document.Assets.Single(asset => asset.Id == binding.AssetId),
                    session.ProjectDirectory);
                await session.Controller.OpenAsync(path, context.CancellationToken);
                if (session.Controller.Snapshot.Error is { } error)
                {
                    throw error;
                }

                ProjectMediaBindingValidator.Validate(document,
                    session.Controller.MediaInfo ?? throw new InvalidDataException("媒体信息不可用。"));
                await session.Analysis.StartAsync(path);
            }

            session.Tick();
        }
        catch
        {
            await ClearPreviewBindingAsync();
            throw;
        }

    }

    internal async Task ClearPreviewBindingAsync()
    {
        await session.Controller.CloseMediaAsync();
        await session.Analysis.ClearAsync();



    }

}
