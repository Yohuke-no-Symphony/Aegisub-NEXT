using AegiNext.Core.Projects;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AegiNext.Desktop.Panels.Styles;

internal sealed class StylesPanelViewModel : ObservableObject
{
    private StyleEditorViewModel? activeEditor;
    private string selectionLabel = string.Empty;

    internal StylesPanelViewModel(WorkbenchSession session)
    {
        Subtitle = new(session);
        Image = new(session);
        Shape = new(session);
        foreach (var editor in new StyleEditorViewModel[] { Subtitle, Image, Shape })
        {
            editor.DraftChanged += (_, _) =>
            {
                if (ReferenceEquals(editor, ActiveEditor))
                {
                    DraftChanged?.Invoke(this, EventArgs.Empty);
                }
            };
        }
    }

    public SubtitleStyleViewModel Subtitle { get; }
    public ImageStyleViewModel Image { get; }
    public ShapeStyleViewModel Shape { get; }
    public StyleEditorViewModel? ActiveEditor
    {
        get => activeEditor;
        private set => SetProperty(ref activeEditor, value);
    }
    public bool HasDrafts => ActiveEditor?.HasDrafts == true;
    public string SelectionLabel
    {
        get => selectionLabel;
        private set => SetProperty(ref selectionLabel, value);
    }
    internal event EventHandler? DraftChanged;

    internal void Load(ProjectDocument document, ProjectLayer? layer, int selectionCount)
    {
        Subtitle.HasCue = layer?.Kind == LayerKind.SUBTITLE;
        ActiveEditor = layer?.Kind switch
        {
            LayerKind.SUBTITLE => Subtitle,
            LayerKind.IMAGE => Image,
            LayerKind.SHAPE => Shape,
            _ => null
        };
        RefreshSelectionLabel(layer, selectionCount);
        if (layer is not null)
        {
            ActiveEditor?.Load(document, layer);
        }
    }

    internal void RefreshSelectionLabel(ProjectLayer? layer, int selectionCount)
    {
        SelectionLabel = layer is null ? Localization.Get("Workbench.NoSelection") : selectionCount > 1
            ? Localization.Format("Workbench.StylePrimarySelection", layer.Name) : string.Empty;
    }

    internal ProjectDocument Prepare(ProjectDocument document, bool preview) => ActiveEditor?.Prepare(document, preview) ?? document;
    internal void Accept() => ActiveEditor?.Accept();
    internal bool RestoreField(string fieldKey) => ActiveEditor?.RestoreField(fieldKey) == true;
    internal void SetDraftState(bool value)
    {
        if (ActiveEditor is { } editor)
        {
            editor.HasDrafts = value;
        }
    }
    internal void Reset()
    {
        Subtitle.Accept();
        Image.Accept();
        Shape.Accept();
        ActiveEditor = null;
    }
}
