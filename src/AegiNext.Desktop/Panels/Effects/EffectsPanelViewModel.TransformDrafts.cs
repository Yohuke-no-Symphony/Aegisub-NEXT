using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Workspace;

namespace AegiNext.Desktop.Panels.Effects;

internal sealed partial class EffectsPanelViewModel
{
    private readonly Dictionary<string, string> operationOriginals = [];
    private bool loadingOperation;
    private ProjectDocument? operationSource;
    private Guid? operationLayer;
    private AnimationTrackTarget operationTarget;
    private Guid? operationIdentity;
    private bool operationOptionsDirty;
    private long operationRevision;
    private long operationPreviewRevision;
    private ProjectDocument? operationPreviewSource;
    private ProjectDocument? operationPreview;

    private IEnumerable<(string Name, NumericValueDraft Draft)> OperationFields =>
    [
        ("OperationStartInput", OperationStart), ("OperationEndInput", OperationEnd),
        ("OperationValueXInput", OperationValueX), ("OperationValueYInput", OperationValueY),
        ("OperationValueZInput", OperationValueZ),
        ("OperationAccelerationInput", OperationAcceleration), ("OperationOrderInput", OperationOrder)
    ];
    internal bool HasOperationDraft => operationOptionsDirty || OperationColorDraft.IsDirty || OperationFields.Any(draftField => operationOriginals.TryGetValue(draftField.Name, out var original) && draftField.Draft.RawText != original);

    private void InitializeOperationDrafts()
    {
        OperationColorDraft.Changed += (_, _) => MarkOperationChanged(false);
        foreach (var field in OperationFields)
        {
            field.Draft.PropertyChanged += (_, e) =>
            {
                if (loadingOperation || session.IsUpdating || e.PropertyName != nameof(NumericValueDraft.RawText))
                {
                    return;
                }
                MarkOperationChanged(false);
            };
        }
    }

    private void MarkOperationChanged(bool optionsChanged = true)
    {
        if (loadingOperation || session.IsUpdating || refreshingChoices)
        {
            return;
        }
        operationOptionsDirty |= optionsChanged;
        operationRevision++;
        session.NotifyTaskInputChanged();
        operationSource ??= session.DocumentSnapshot;
        operationLayer ??= SelectedLayer?.Id;
        operationTarget = Target;
        operationIdentity ??= SelectedOperation?.Id;
        session.RefreshMaskPreview();
    }

    public bool RestoreOperationField(string? name)
    {
        name = name == "OperationValueInput" ? "OperationValueXInput" : name;
        var field = OperationFields.FirstOrDefault(field => field.Name == name);
        if (field.Draft is null || !operationOriginals.TryGetValue(field.Name, out var original))
        {
            return false;
        }
        loadingOperation = true;
        try
        {
            field.Draft.RawText = original;
        }
        finally
        {
            loadingOperation = false;
        }
        operationRevision++;
        session.RefreshMaskPreview();
        return true;
    }

    internal ProjectDocument PrepareOperationDraft(ProjectDocument document)
    {
        if (!HasOperationDraft)
        {
            return document;
        }
        if (!ReferenceEquals(operationSource, session.DocumentSnapshot) || operationLayer != SelectedLayer?.Id)
        {
            throw new InvalidOperationException(Localization.Get("Workbench.SubtitleDraftConflict"));
        }
        document = AnimationEditOperations.NormalizeRotation(document, operationLayer!.Value, operationTarget);
        var layer = document.Layers.Single(layer => layer.Id == operationLayer);
        var track = layer.Tracks.Single(track => track.Target == operationTarget);
        var operation = track.Transforms.Single(operation => operation.Id == operationIdentity);
        double Read(string key, NumericValueDraft field)
        {
            if (double.TryParse(field.RawText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out var value) && double.IsFinite(value))
            {
                return value;
            }
            session.ViewModel.InvalidPanelId = session.AnimationOptionsPanelId ?? "effects";
            session.ViewModel.InvalidFieldKey = key;
            throw new InvalidDataException(Localization.Get("Workbench.InvalidValue"));
        }
        var start = Read("OperationStartInput", OperationStart);
        var end = Read("OperationEndInput", OperationEnd);
        var acceleration = CanEditOperationExponent ? Read("OperationAccelerationInput", OperationAcceleration) : operation.Curve!.Exponent;
        var order = Read("OperationOrderInput", OperationOrder);
        if (end < start || acceleration < 0 || operation.Curve is not null && acceleration <= 0 ||
            order != Math.Truncate(order) || order < 1 || order > track.Transforms.Length)
        {
            throw new InvalidDataException(Localization.Get("Workbench.InvalidValue"));
        }
        AnimationValue value;
        if (operation.Value.IsColor)
        {
            if (!OperationColorDraft.TryCommit(out var color))
            {
                session.ViewModel.InvalidFieldKey = "OperationColorInput";
                throw new InvalidDataException(OperationColorDraft.Error);
            }
            value = color;
        }
        else
        {
            var x = Read("OperationValueXInput", OperationValueX);
            value = operation.Value.IsVector3 ? AnimationValue.FromVector3(new(x, Read("OperationValueYInput", OperationValueY), Read("OperationValueZInput", OperationValueZ))) :
                operation.Value.IsVector ? new ScenePoint(x, Read("OperationValueYInput", OperationValueY)) : x;
        }
        var changed = operation with
        {
            Start = operationOriginals["OperationStartInput"] == OperationStart.RawText ? operation.Start : new MediaTime((long)Math.Round(start * 1000000), 1000000),
            End = operationOriginals["OperationEndInput"] == OperationEnd.RawText ? operation.End : new MediaTime((long)Math.Round(end * 1000000), 1000000), Value = value,
            Acceleration = operation.Curve is null ? acceleration : operation.Acceleration,
            Curve = operation.Curve is { } curve ? curve with
            {
                Interpolation = (KeyframeInterpolation)OperationCurveInterpolation, Exponent = acceleration, Reverse = OperationCurveReverse
            } : null,
            ComponentMask = operationComponentMask, Mode = operationIsRelative ? AnimationTransformMode.MULTIPLY_BY : AnimationTransformMode.INTERPOLATE_TO
        };
        var operations = track.Transforms.Remove(operation).Insert((int)order - 1, changed);
        return WorkspaceDraftOperations.UpdateLayer(document, layer.Id, candidate => candidate with
        {
            Tracks = candidate.Tracks.SetItem(candidate.Tracks.IndexOf(track), track with { Transforms = operations })
        });
    }

    internal ProjectDocument OverlayOperationDraft(ProjectDocument document)
    {
        if (!HasOperationDraft)
        {
            operationPreviewSource = null;
            operationPreview = null;
            return document;
        }
        if (ReferenceEquals(operationPreviewSource, document) && operationPreviewRevision == operationRevision && operationPreview is { } cached)
        {
            return cached;
        }
        try
        {
            var result = PrepareOperationDraft(document);
            if (!ReferenceEquals(result, document))
            {
                ProjectValidator.Validate(result);
            }
            operationPreviewSource = document;
            operationPreviewRevision = operationRevision;
            operationPreview = result;
            return result;
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or ArgumentException or OverflowException)
        {
            return document;
        }
    }

    internal void RebindRelocatedSource(ProjectDocument document)
    {
        if (operationSource is not null)
        {
            operationSource = document;
        }
    }

    internal void AcceptOperationDraft()
    {
        operationPreviewSource = null;
        operationPreview = null;
        operationSource = null;
        operationLayer = null;
        operationIdentity = null;
        operationOriginals.Clear();
        operationOptionsDirty = false;
        OperationColorDraft.Load(OperationColorDraft.Value);
    }
}
