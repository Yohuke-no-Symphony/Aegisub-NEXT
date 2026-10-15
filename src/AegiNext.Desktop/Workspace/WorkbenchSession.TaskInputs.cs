using System.ComponentModel;
using AegiNext.Application;
using AegiNext.Desktop.Editing;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    private long taskInputRevision;
    private readonly HashSet<SubtitleRow> observedTaskRows = [];

    internal long TaskInputRevision => taskInputRevision;
    internal Task DispatchTaskCompletionAsync(Action action) => dispatch(action, CancellationToken.None);
    internal bool HasProjectDrafts => ViewModel.Styles.HasDrafts || effectsDirty && SelectedLayer is not null ||
        Details.HasDrafts || MaskEditing.HasDrafts ||
        ViewModel.Effects.HasOperationDraft || ViewModel.Effects.HasPropertyDrafts || ViewModel.Effects.HasInstanceDrafts || ViewModel.Subtitles.Rows.Any(row => row.IsDirty);

    internal void NotifyTaskInputChanged()
    {
        if (!IsUpdating)
        {
            taskInputRevision++;
            RefreshTitle();
        }
    }

    private void InitializeTaskInputTracking()
    {
        Details.Changed += OnTaskDetailsChanged;
        SceneEditing.Changed += OnTaskSceneChanged;
        editor.StateChanged += OnTaskDocumentChanged;
        ViewModel.Subtitles.PropertyChanged += OnTaskRowsChanged;
        ObserveTaskRows();
    }

    private void DisposeTaskInputTracking()
    {
        Details.Changed -= OnTaskDetailsChanged;
        SceneEditing.Changed -= OnTaskSceneChanged;
        editor.StateChanged -= OnTaskDocumentChanged;
        ViewModel.Subtitles.PropertyChanged -= OnTaskRowsChanged;
        foreach (var row in observedTaskRows)
        {
            row.PropertyChanged -= OnTaskRowChanged;
        }
        observedTaskRows.Clear();
    }

    private void OnTaskDocumentChanged(object? sender, ProjectEditorChangedEventArgs e)
    {
        if (e.Kind == ProjectEditorChangeKind.DOCUMENT)
        {
            taskInputRevision++;
        }
    }

    private void OnTaskDetailsChanged(object? sender, EventArgs e) => NotifyTaskInputChanged();
    private void OnTaskSceneChanged(object? sender, EventArgs e)
    {
        NotifyTaskInputChanged();
    }

    private void OnTaskRowsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.Subtitles.Rows))
        {
            ObserveTaskRows();
        }
    }

    private void ObserveTaskRows()
    {
        var rows = ViewModel.Subtitles.Rows.ToHashSet();
        foreach (var row in observedTaskRows.Where(row => !rows.Contains(row)).ToArray())
        {
            row.PropertyChanged -= OnTaskRowChanged;
            observedTaskRows.Remove(row);
        }
        foreach (var row in rows.Where(row => !observedTaskRows.Contains(row)))
        {
            row.PropertyChanged += OnTaskRowChanged;
            observedTaskRows.Add(row);
        }
    }

    private void OnTaskRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SubtitleRow.Text) or nameof(SubtitleRow.StartText) or nameof(SubtitleRow.EndText))
        {
            NotifyTaskInputChanged();
        }
    }
}
