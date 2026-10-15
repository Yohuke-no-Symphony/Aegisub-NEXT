using AegiNext.Application.Presets;
using AegiNext.Core.Effects;
using AegiNext.Core.Projects;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Workspace.Diagnostics;
using System.Collections.Immutable;
using AegiNext.Core.Editing;
using AegiNext.Desktop.Rendering;

namespace AegiNext.Desktop.Workspace;

internal sealed class EffectScriptLibraryCoordinator(WorkbenchSession session, IWorkbenchDialogService dialogs)
{
    private int queuedOperations;
    private EffectScriptChoice[] choices = [];
    private EffectScriptPresetDocument? analyzedLibrary;
    private readonly HashSet<Guid> subtitleOnlyPresets = [];
    private readonly Lock operationsGate = new();
    private readonly HashSet<Task> operations = [];
    private (ProjectDocument Document, string Source, string Parameters, string TargetIds, AnimationTrackTarget? Context)? availabilityKey;
    private EffectPresetApplyAvailability cachedAvailability;
    internal string? AvailabilityError { get; private set; }
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
    internal bool IsBusy => queuedOperations > 0 || session.ApplicationContext.EffectsBusy;

    internal void Initialize() => Queue(async () =>
    {
        await session.ApplicationContext.Initialization;
        RefreshChoices();
    });

    internal void Queue(Func<Task> action, Action<WorkbenchLogEntry>? onFailure = null)
    {
        queuedOperations++;
        session.ViewModel.EffectPresets.RefreshAvailability();
        var operation = RunAsync(action, onFailure);
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

    private async Task RunAsync(Func<Task> action, Action<WorkbenchLogEntry>? onFailure)
    {
        try
        {
            if (!session.IsClosing)
            {
                await session.RunCommandAsync(action, onFailure);
            }
        }
        finally
        {
            queuedOperations--;
            session.ViewModel.EffectPresets.RefreshAvailability();
        }
    }

    internal void RefreshChoices()
    {
        var library = session.EffectScriptLibrary.Snapshot;
        if (!ReferenceEquals(analyzedLibrary, library))
        {
            subtitleOnlyPresets.Clear();
            foreach (var preset in library.Presets)
            {
                if (EffectScriptCompatibility.RequiresSubtitle(EffectScriptParser.Parse(preset.Source)))
                {
                    subtitleOnlyPresets.Add(preset.Id);
                }
            }
            analyzedLibrary = library;
        }
        choices = BuiltinEffectScripts.Templates.Select(template => new EffectScriptChoice(template.Script.Id,
            BuiltinName(template.Script.Id), template.Source, EffectScriptCompatibility.RequiresSubtitle(template.Script)))
            .Concat(library.Presets.Select(preset => new EffectScriptChoice(preset.Id.ToString("D"), preset.Name,
                preset.Source, subtitleOnlyPresets.Contains(preset.Id))))
            .ToArray();
        session.ViewModel.EffectPresets.RefreshCatalog(choices);
    }

    internal Task ApplySelectedAsync()
    {
        var preset = session.ViewModel.EffectPresets.SelectedPreset;
        if (Availability(preset?.Id) == EffectPresetApplyAvailability.READY &&
            choices.FirstOrDefault(choice => choice.Id == preset!.Id && choice.Source == preset.Source) is { } selected)
        {
            if (!preset!.Parameters.TryCapture(out var parameters))
            {
                return Task.CompletedTask;
            }
            return ApplyAsync(selected.Source, selected.Name, parameters);
        }
        return Task.CompletedTask;
    }

    internal EffectPresetApplyAvailability Availability(string? presetId)
    {
        if (session.IsProjectBusy || session.IsClosing || IsBusy)
        {
            return EffectPresetApplyAvailability.BUSY;
        }
        var preset = choices.FirstOrDefault(choice => choice.Id == presetId);
        var targets = SelectedTargets();
        if (preset is null || targets.Length == 0)
        {
            return EffectPresetApplyAvailability.NO_SELECTION;
        }
        var card = session.ViewModel.EffectPresets.Items.FirstOrDefault(item => item.Id == presetId);
        if (card is not null && !card.Parameters.TryCapture(out _))
        {
            return EffectPresetApplyAvailability.INVALID_PARAMETERS;
        }
        var parameters = card?.Parameters.TryCapture(out var captured) == true ? captured :
            ImmutableDictionary<string, EffectScriptParameterValue>.Empty;
        var document = session.Editor.Snapshot;
        var signature = EffectScriptParameterIdentity.Create(parameters);
        var ids = string.Join(',', targets.Select(layer => layer.Id));
        var context = session.SceneEditing.Target.TextRangeId.HasValue ? session.SceneEditing.Target : (AnimationTrackTarget?)null;
        if (availabilityKey is { } key && ReferenceEquals(key.Document, document) && key.Source == preset.Source &&
            key.Parameters == signature && key.TargetIds == ids && key.Context == context)
        {
            return cachedAvailability;
        }
        availabilityKey = (document, preset.Source, signature, ids, context);
        AvailabilityError = null;
        try
        {
            var script = EffectScriptParser.Parse(preset.Source);
            foreach (var layer in targets)
            {
                var range = LayerAnimationTiming.GetRange(layer);
                var instance = new EffectInstance { Source = preset.Source, Parameters = parameters, Start = range.Minimum,
                    End = range.Maximum, TargetContext = context };
                var subtitle = layer.SubtitleId is { } cueId ? document.Subtitles.FirstOrDefault(line => line.Id == cueId) : null;
                _ = EffectScriptCompiler.CompileInstance(script, instance, layer, subtitle);
            }
            cachedAvailability = EffectPresetApplyAvailability.READY;
        }
        catch (Exception error) when (error is EffectScriptException or InvalidDataException or ArgumentException or InvalidOperationException)
        {
            AvailabilityError = error.Message;
            cachedAvailability = error.Message.Contains("字幕", StringComparison.Ordinal)
                ? EffectPresetApplyAvailability.REQUIRES_SUBTITLE : EffectPresetApplyAvailability.INVALID_TARGET;
        }
        return cachedAvailability;
    }

    internal Task ApplyAsync(string source, string? name = null,
        ImmutableDictionary<string, EffectScriptParameterValue>? parameters = null)
    {
        if (session.IsProjectBusy || session.IsClosing || !session.TryCommitDrafts())
        {
            return Task.CompletedTask;
        }
        var target = session.SceneEditing.Target;
        AnimationTrackTarget? context = target.TextRangeId is not null || target.State != SubtitleAnimationState.NORMAL ? target : null;
        var layerIds = SelectedTargets().Select(layer => layer.Id).ToArray();
        if (layerIds.Length == 0)
        {
            return Task.CompletedTask;
        }
        session.ViewModel.CancelGestures();
        session.ClearKeyframeSelection();
        return session.ApplicationContext.Tasks.Submit(new ApplyEffectScriptTask(session, source, layerIds,
            session.Editor.Snapshot, session.TaskInputRevision, context, name, parameters)).Completion;
    }

    private ProjectLayer[] SelectedTargets()
    {
        var selected = session.SceneEditing.Target.TextRangeId is not null && session.SelectedLayer is { } rangeLayer
            ? new HashSet<Guid> { rangeLayer.Id } : session.TimelineClipIds().ToHashSet();
        return session.Editor.Snapshot.Layers.Where(layer => selected.Contains(layer.Id)).ToArray();
    }

    internal async Task UpsertAsync(EffectScriptPreset preset)
    {
        await session.ApplicationContext.RunEffectOperationAsync(() => session.EffectScriptLibrary.UpsertAsync(preset));
        RefreshChoices();
        session.NotifyEffectLibraryChanged();
        session.LogInfo("Effects", Localization.Get("Workbench.SavePreset"), preset.Name);
    }

    internal async Task DeleteAsync(Guid id)
    {
        await session.ApplicationContext.RunEffectOperationAsync(() => session.EffectScriptLibrary.RemoveAsync(id));
        RefreshChoices();
        session.NotifyEffectLibraryChanged();
    }

    internal async Task ImportAsync()
    {
        var path = await dialogs.OpenFileAsync("ImportEffectScripts", "EffectScriptFiles", ["*.aegifx"]);
        if (path is null)
        {
            return;
        }
        await session.ApplicationContext.Tasks.Submit(new ImportEffectScriptsTask(session, path)).Completion;
        RefreshChoices();
        session.NotifyEffectLibraryChanged();
    }

    internal async Task ExportAsync(EffectScriptPreset preset)
    {
        var script = EffectScriptParser.Parse(preset.Source);
        var path = await dialogs.SaveFileAsync("ExportEffectScripts", "EffectScriptFiles", ["*.aegifx"], ".aegifx", script.Id + ".aegifx");
        if (path is not null)
        {
            await session.ApplicationContext.Tasks.Submit(new ExportEffectScriptTask(session, path, preset.Source)).Completion;
        }
    }

    private static string BuiltinName(string id) => Localization.Get("Workbench." + (id switch
    {
        "fade-in-out" => "Fade", "fade-in" => "FadeIn", "fade-out" => "FadeOut",
        "pop-in" => "Pop", "pop-out" => "PopOut", "slide-in" => "Slide", "slide-out" => "SlideOut",
        "letter-bounce" => "LetterBounce", "letter-pulse" => "LetterPulse",
        _ => throw new ArgumentOutOfRangeException(nameof(id))
    }));
}
