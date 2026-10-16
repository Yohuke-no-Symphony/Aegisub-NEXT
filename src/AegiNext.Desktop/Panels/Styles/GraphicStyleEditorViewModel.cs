using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Workspace;

namespace AegiNext.Desktop.Panels.Styles;

internal abstract class GraphicStyleEditorViewModel : StyleEditorViewModel
{
    protected GraphicStyleEditorViewModel(WorkbenchSession session) : base(session)
    {
        Position.Changed += (_, _) => MarkDraftChanged();
    }

    public LayerPositionDraft Position { get; } = new();
    internal abstract ScenePoint PositionSize(ProjectLayer layer);

    /// <inheritdoc />
    public override void Accept()
    {
        Position.ClearEdits();
        base.Accept();
    }
    internal override Action CaptureDraftIntent()
    {
        var restoreFields = base.CaptureDraftIntent();
        var restorePosition = Position.CaptureEditState();
        return () =>
        {
            restorePosition();
            restoreFields();
        };
    }

    protected void ObserveGraphicNumber(NumericValueDraft draft, string field)
    {
        draft.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(NumericValueDraft.RawText))
            {
                MarkGraphicFieldChanged(field);
            }
        };
    }

    protected void MarkGraphicFieldChanged(string field)
    {
        RecordDraftField(field);
        MarkDraftChanged();
    }

    protected Action CaptureGraphicNumbers(params (string Field, NumericValueDraft Draft)[] fields)
    {
        var captured = fields.Select(field => (field.Field, field.Draft, Text: field.Draft.RawText,
            WasRecorded: DraftFields.Contains(field.Field))).ToArray();
        return () =>
        {
            Session.RestoreGraphicStyleField(() =>
            {
                foreach (var field in captured)
                {
                    field.Draft.RawText = field.Text;
                }
            });
            foreach (var field in captured)
            {
                if (field.WasRecorded)
                {
                    RecordDraftField(field.Field);
                }
                else
                {
                    ForgetDraftField(field.Field);
                }
            }
        };
    }

    protected sealed override void LoadCore(ProjectDocument document, ProjectLayer layer)
    {
        LoadGraphicCore(document, layer);
        Position.Load(layer.Position ?? new(), Session.GraphicPositionGeometry(document, layer, PositionSize(layer)));
    }

    protected sealed override ProjectDocument PrepareCore(ProjectDocument document, ProjectLayer layer, bool preview)
    {
        var position = Session.ReadGraphicPositionDraft(layer, Position);
        var prepared = PrepareGraphicCore(document, layer, preview);
        return WorkspaceDraftOperations.UpdateLayer(prepared, layer.Id, current => current with
        {
            Position = position,
            Transform = Position.PrepareTransform(current.Transform)
        });
    }

    protected override void RefreshGeometry(ProjectDocument document, ProjectLayer layer)
    {
        Position.UpdateGeometry(Session.GraphicPositionGeometry(document, layer, PositionSize(layer)));
    }

    protected override void MarkDraftChanged()
    {
        if (!IsLoading && !Session.IsUpdating && IsActive && Session.SelectedLayer is { } layer)
        {
            RefreshGeometry(Session.DocumentSnapshot, layer);
        }
        base.MarkDraftChanged();
    }

    public sealed override bool RestoreField(string fieldKey)
    {
        if (!IsActive)
        {
            return false;
        }
        if (fieldKey is "AnchorXInput" or "AnchorYInput" or "PivotXInput" or "PivotYInput" or "OffsetXInput" or "OffsetYInput")
        {
            Session.RestoreGraphicStyleField(() => Position.RestoreField(fieldKey));
            return true;
        }
        return RestoreGraphicField(fieldKey);
    }

    protected abstract void LoadGraphicCore(ProjectDocument document, ProjectLayer layer);
    protected abstract ProjectDocument PrepareGraphicCore(ProjectDocument document, ProjectLayer layer, bool preview);
    protected abstract bool RestoreGraphicField(string fieldKey);
}
