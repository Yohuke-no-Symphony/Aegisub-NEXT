using System.Globalization;
using System.Collections.Immutable;
using AegiNext.Core.Projects;
using Avalonia.Threading;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    private ProjectDocument? inspectorPreview;
    private ProjectDocument? inspectorPreviewSource;
    private Guid? inspectorPreviewLayerId;
    private ImmutableArray<Guid> inspectorPreviewLayerIds = [];
    private bool inspectorPreviewQueued;
    private int inspectorPreviewRevision;

    internal ProjectDocument PreviewDocument
    {
        get
        {
            var source = DocumentSnapshot;
            var document = inspectorPreview is { } preview && ReferenceEquals(inspectorPreviewSource, source) &&
                inspectorPreviewLayerId == SelectedLayerId && inspectorPreviewLayerIds.SequenceEqual(TimelineClipIds())
                    ? preview : Details?.OverlayPreview(source) ?? source;
            document = MaskEditing?.Overlay(document) ?? document;
            return OverlayTimingPreview(PropertyEditing.Overlay(ViewModel.Effects.OverlayOperationDraft(document)));
        }
    }

    private void QueueInspectorPreview(bool supersedePending = false)
    {
        if (supersedePending)
        {
            inspectorPreviewRevision++;
            inspectorPreviewQueued = false;
        }

        if (inspectorPreviewQueued || IsUpdating || closing || IsProjectBusy)
        {
            return;
        }

        inspectorPreviewQueued = true;
        var revision = inspectorPreviewRevision;
        var source = DocumentSnapshot;
        var layerId = SelectedLayerId;
        var layerIds = TimelineClipIds();
        var target = SceneEditing.DraftTarget;
        Dispatcher.UIThread.Post(() =>
        {
            if (revision != inspectorPreviewRevision)
            {
                return;
            }
            inspectorPreviewQueued = false;
            if (closing || IsProjectBusy || IsUpdating || layerId != SelectedLayerId || target != SceneEditing.DraftTarget ||
                !ReferenceEquals(source, DocumentSnapshot) || !layerIds.SequenceEqual(TimelineClipIds()))
            {
                return;
            }

            var invalidPanel = ViewModel.InvalidPanelId;
            var invalidField = ViewModel.InvalidFieldKey;
            try
            {
                var candidate = PrepareInspectorDrafts(Details.OverlayPreview(source), true);
                ProjectValidator.Validate(candidate);
                inspectorPreview = candidate;
                inspectorPreviewSource = source;
                inspectorPreviewLayerId = layerId;
                inspectorPreviewLayerIds = layerIds;
                RefreshPositionPreview(candidate);
                RefreshEditingPreview();
            }
            catch (Exception error) when (error is InvalidDataException or ArgumentException or InvalidOperationException or OverflowException or FormatException)
            {
                // Invalid raw drafts retain the most recent valid scene until the user repairs or restores the field.
            }
            finally
            {
                ViewModel.InvalidPanelId = invalidPanel;
                ViewModel.InvalidFieldKey = invalidField;
            }
        }, DispatcherPriority.Background);
    }

    private void ClearInspectorPreview()
    {
        inspectorPreviewRevision++;
        inspectorPreviewQueued = false;
        inspectorPreview = null;
        inspectorPreviewSource = null;
        inspectorPreviewLayerId = null;
        inspectorPreviewLayerIds = [];
    }

    private void RefreshPositionPreview(ProjectDocument document)
    {
        if (!stylesDirty)
        {
            return;
        }
        var layer = document.Layers.FirstOrDefault(value => value.Id == SelectedLayerId);
        if (layer?.SubtitleId is not { } id)
        {
            return;
        }
        var subtitle = document.Subtitles.Single(line => line.Id == id);
        var placement = ResolvePlacement(document, layer);
        using var updateLease = BeginWorkbenchUpdate();
        if (ViewModel.Styles.Subtitle.Position.IsExplicit || ViewModel.Styles.Subtitle.Position.HasEdits)
        {
            ViewModel.Styles.Subtitle.Position.UpdateGeometry(PreparePositionGeometry(placement.Geometry, layer));
            return;
        }
        ViewModel.Styles.Subtitle.Position.Load(subtitle.Style, placement.Position, placement.BasePosition is not null,
            PreparePositionGeometry(placement.Geometry, layer));
    }

    private string SynchronizeNumericText(string text, decimal? value)
    {
        if (!IsUpdating && (value is null && string.IsNullOrWhiteSpace(text) ||
            decimal.TryParse(text, NumberStyles.Float, InterfaceCulture, out var parsed) && parsed == value))
        {
            return text;
        }
        return value?.ToString(InterfaceCulture) ?? string.Empty;
    }
}
