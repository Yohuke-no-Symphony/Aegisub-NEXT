using AegiNext.Core.Projects;
using AegiNext.Desktop.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AegiNext.Desktop.Panels.Styles;

internal abstract class StyleEditorViewModel(WorkbenchSession session) : ObservableObject, IStyleEditor
{
    private bool loading;

    protected WorkbenchSession Session { get; } = session;
    protected bool IsLoading => loading;
    public abstract LayerKind Kind { get; }
    public Guid? LayerId { get; private set; }
    public bool HasDrafts { get; internal set; }
    internal event EventHandler? DraftChanged;
    internal bool IsActive => ReferenceEquals(Session.ViewModel.Styles.ActiveEditor, this) && LayerId == Session.SelectedLayerId;

    public void Load(ProjectDocument document, ProjectLayer layer)
    {
        if (layer.Kind != Kind)
        {
            throw new ArgumentException("The style editor cannot load this clip type.", nameof(layer));
        }
        if (HasDrafts && LayerId == layer.Id)
        {
            return;
        }
        loading = true;
        try
        {
            LayerId = layer.Id;
            HasDrafts = false;
            LoadCore(document, layer);
        }
        finally
        {
            loading = false;
        }
    }

    public ProjectDocument Prepare(ProjectDocument document, bool preview)
    {
        if (!HasDrafts || !IsActive)
        {
            return document;
        }
        var layer = document.Layers.Single(value => value.Id == LayerId && value.Kind == Kind);
        return PrepareCore(document, layer, preview);
    }

    public void Accept() => HasDrafts = false;
    public abstract bool RestoreField(string fieldKey);
    internal virtual Action? CaptureNumericGesture(string? fieldKey) => null;
    protected abstract void LoadCore(ProjectDocument document, ProjectLayer layer);
    protected abstract ProjectDocument PrepareCore(ProjectDocument document, ProjectLayer layer, bool preview);

    protected void MarkDraftChanged()
    {
        if (loading || Session.IsUpdating || !IsActive)
        {
            return;
        }
        HasDrafts = true;
        DraftChanged?.Invoke(this, EventArgs.Empty);
    }

    public void CommitDrafts()
    {
        if (IsActive)
        {
            Session.TryCommitDrafts(false);
        }
    }
}
