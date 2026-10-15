using System.ComponentModel;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;

namespace AegiNext.Desktop.Workspace;

internal sealed class ClipMaskPositionEditingCoordinator
{
    private const string FIELD_PREFIX = "MaskPlacement_";
    private readonly WorkbenchSession session;
    private readonly (string Key, NumericValueDraft Draft)[] fields;
    private readonly Dictionary<string, string> loadedText = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> editingPanels = new(StringComparer.Ordinal);
    private ProjectDocument? draftSource;
    private AnimationEditTarget? draftTarget;
    private Guid? layerId;
    private string activePanel = "masks";
    private bool loading;

    internal ClipMaskPositionEditingCoordinator(WorkbenchSession session)
    {
        this.session = session;
        fields =
        [
            ("AnchorXInput", Draft.AnchorX), ("AnchorYInput", Draft.AnchorY),
            ("PivotXInput", Draft.PivotX), ("PivotYInput", Draft.PivotY),
            ("OffsetXInput", Draft.OffsetX), ("OffsetYInput", Draft.OffsetY)
        ];
        foreach (var field in fields)
        {
            field.Draft.PropertyChanging += OnFieldChanging;
            loadedText[field.Key] = field.Draft.RawText;
        }
        Draft.Changed += (_, _) => OnDraftChanged();
    }

    internal LayerPositionDraft Draft { get; } = new()
    {
        PivotMinimum = -(decimal)ClipMaskPlacementGeometry.MAX_NORMALIZED_PIVOT,
        PivotMaximum = (decimal)ClipMaskPlacementGeometry.MAX_NORMALIZED_PIVOT
    };
    internal bool HasDrafts => fields.Any(positionField => positionField.Draft.RawText != loadedText[positionField.Key]);

    internal static string FieldKey(string localKey) => FIELD_PREFIX + localKey;

    internal static string? LocalFieldKey(string? fieldKey) => fieldKey?.StartsWith(FIELD_PREFIX, StringComparison.Ordinal) == true
        ? fieldKey[FIELD_PREFIX.Length..] : null;

    internal void BeginEdit(string panelId) => activePanel = panelId;

    internal void Refresh(ProjectLayer? layer)
    {
        if (layer?.Mask is not { } mask)
        {
            loading = true;
            try
            {
                Draft.UpdateGeometry(null);
                layerId = null;
                Accept();
            }
            finally
            {
                loading = false;
            }
            return;
        }
        var evaluated = session.EvaluateMask(layer, session.AnimationTarget?.LocalTime ?? new(0))!;
        var transform = evaluated.Transform;
        var geometry = new LayerPositionDraftGeometry(new(session.DocumentSnapshot.Width, session.DocumentSnapshot.Height),
            mask.ReferenceFrame.Origin, mask.ReferenceFrame.Size,
            new(transform.Position.X, transform.Position.Y, transform.Scale.X, transform.Scale.Y, transform.Rotation));
        if (layerId == layer.Id && HasDrafts && ReferenceEquals(draftSource, session.DocumentSnapshot))
        {
            Draft.UpdateGeometry(geometry);
            return;
        }
        loading = true;
        try
        {
            layerId = layer.Id;
            Draft.Load(mask.Placement, geometry);
            Accept();
        }
        finally
        {
            loading = false;
        }
    }

    internal ProjectDocument Prepare(ProjectDocument document)
    {
        if (!HasDrafts || draftTarget is not { } target)
        {
            return document;
        }
        if (!ReferenceEquals(draftSource, session.DocumentSnapshot) || target.LayerId != session.SelectedLayerId || target.LayerId != layerId)
        {
            throw new InvalidOperationException(Localization.Get("Workbench.SubtitleDraftConflict"));
        }
        if (Draft.Validate() is { } field)
        {
            session.ViewModel.InvalidPanelId = editingPanels.GetValueOrDefault(field, activePanel);
            session.ViewModel.InvalidFieldKey = FieldKey(field);
            throw new InvalidDataException(Localization.Get("Workbench.MaskPlacement"));
        }
        var placement = Draft.CreatePosition()!;
        return WorkspaceDraftOperations.UpdateLayer(document, target.LayerId, layer => layer with
        {
            Mask = layer.Mask! with { Placement = placement }
        });
    }

    internal void Accept()
    {
        foreach (var field in fields)
        {
            loadedText[field.Key] = field.Draft.RawText;
        }
        editingPanels.Clear();
        draftSource = null;
        draftTarget = null;
    }

    internal void RebindRelocatedSource(ProjectDocument document)
    {
        if (draftSource is not null)
        {
            draftSource = document;
        }
    }

    internal bool Restore(string? fieldKey)
    {
        if (LocalFieldKey(fieldKey) is not { } localKey || !fields.Any(field => field.Key == localKey))
        {
            return false;
        }
        loading = true;
        try
        {
            Draft.RestoreField(localKey);
        }
        finally
        {
            loading = false;
        }
        editingPanels.Remove(localKey);
        if (!HasDrafts)
        {
            draftSource = null;
            draftTarget = null;
        }
        session.NotifyEffectPropertyDraftChanged();
        session.ClearEffectPropertyDraftError(fieldKey!);
        session.RefreshMaskPreview();
        return true;
    }

    private void OnFieldChanging(object? sender, PropertyChangingEventArgs args)
    {
        if (loading || session.IsUpdating || args.PropertyName != nameof(NumericValueDraft.RawText))
        {
            return;
        }
        var field = fields.First(field => ReferenceEquals(field.Draft, sender));
        editingPanels[field.Key] = activePanel;
        FreezeTarget();
    }

    private void FreezeTarget()
    {
        draftSource ??= session.DocumentSnapshot;
        draftTarget ??= session.AnimationTarget;
        session.SceneEditing.DraftTarget ??= draftTarget;
    }

    private void OnDraftChanged()
    {
        if (loading || session.IsUpdating)
        {
            return;
        }
        FreezeTarget();
        session.NotifyEffectPropertyDraftChanged();
        _ = session.RunCommandAsync(session.PauseForSceneEditAsync);
        session.RefreshMaskPreview();
    }
}
