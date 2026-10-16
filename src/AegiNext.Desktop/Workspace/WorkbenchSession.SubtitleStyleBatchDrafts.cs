using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    private ProjectDocument PrepareSubtitleStyleBatchDrafts(ProjectDocument source, ProjectDocument prepared,
        ProjectLayer primary, IReadOnlyList<Guid> targets)
    {
        var before = source.Subtitles.Single(line => line.Id == primary.SubtitleId).Style;
        var after = prepared.Subtitles.Single(line => line.Id == primary.SubtitleId).Style;
        var vm = ViewModel.Styles.Subtitle;
        var fields = vm.DraftFields;
        var boldChanged = before.Bold != after.Bold || fields.Contains("Bold");
        var italicChanged = before.Italic != after.Italic || fields.Contains("Italic");
        var fontChanged = before.FontFamily != after.FontFamily || vm.FontSelectionCommitted || fields.Contains("FontDraft") ||
            !boldChanged && !italicChanged && (before.FontVariant != after.FontVariant || before.FontAssetId != after.FontAssetId);
        foreach (var id in targets.Where(id => id != primary.Id))
        {
            var layer = prepared.Layers.Single(layer => layer.Id == id);
            var line = prepared.Subtitles.Single(line => line.Id == layer.SubtitleId);
            var current = line.Style;
            var style = current with
            {
                FontFamily = fontChanged ? after.FontFamily : current.FontFamily,
                FontVariant = fontChanged ? after.FontVariant : current.FontVariant,
                FontAssetId = fontChanged ? after.FontAssetId : current.FontAssetId,
                FontSize = StyleFieldValue(before.FontSize, after.FontSize, current.FontSize, fields, "FontSize", "FontSizeText"),
                LineHeight = StyleFieldValue(before.LineHeight, after.LineHeight, current.LineHeight, fields, "LineHeight", "LineHeightText"),
                Bold = StyleFieldValue(before.Bold, after.Bold, current.Bold, fields, "Bold"),
                Italic = StyleFieldValue(before.Italic, after.Italic, current.Italic, fields, "Italic"),
                WrapMode = StyleFieldValue(before.WrapMode, after.WrapMode, current.WrapMode, fields, "WrapMode"),
                Alignment = StyleFieldValue(before.Alignment, after.Alignment, current.Alignment, fields, "Alignment", "AlignmentSelectionCommitted"),
                TextAlign = vm.AlignmentSelectionCommitted ? null : current.TextAlign,
                ShadowOffset = new(
                    StyleFieldValue(before.ShadowOffset.X, after.ShadowOffset.X, current.ShadowOffset.X, fields, "ShadowX", "ShadowXText"),
                    StyleFieldValue(before.ShadowOffset.Y, after.ShadowOffset.Y, current.ShadowOffset.Y, fields, "ShadowY", "ShadowYText")),
                ShadowBlur = StyleFieldValue(before.ShadowBlur, after.ShadowBlur, current.ShadowBlur, fields, "ShadowBlur", "ShadowBlurText"),
                ShadowColor = PatchStyleColor(vm.ShadowDraft, before.ShadowColor, after.ShadowColor, current.ShadowColor),
                Margins = current.Margins with
                {
                    Left = StyleFieldValue(before.Margins.Left, after.Margins.Left, current.Margins.Left, fields, "Margins.Left"),
                    Right = StyleFieldValue(before.Margins.Right, after.Margins.Right, current.Margins.Right, fields, "Margins.Right"),
                    Vertical = StyleFieldValue(before.Margins.Vertical, after.Margins.Vertical, current.Margins.Vertical, fields, "Margins.Vertical")
                },
                Scaling = current.Scaling with
                {
                    ReferenceWidth = StyleFieldValue(before.Scaling.ReferenceWidth, after.Scaling.ReferenceWidth,
                        current.Scaling.ReferenceWidth, fields, "Scaling.ReferenceWidth"),
                    ReferenceHeight = StyleFieldValue(before.Scaling.ReferenceHeight, after.Scaling.ReferenceHeight,
                        current.Scaling.ReferenceHeight, fields, "Scaling.ReferenceHeight"),
                    MatchWidthOrHeight = StyleFieldValue(before.Scaling.MatchWidthOrHeight, after.Scaling.MatchWidthOrHeight,
                        current.Scaling.MatchWidthOrHeight, fields, "Scaling.MatchWidthOrHeight")
                }
            };
            if (fontChanged && after.FontVariant is not null)
            {
                style = style with { Bold = after.Bold, Italic = after.Italic };
            }
            else if (!fontChanged)
            {
                if (boldChanged)
                {
                    style = Fonts.ToggleBold(style, after.Bold).ApplyTo(style);
                }
                if (italicChanged)
                {
                    style = Fonts.ToggleItalic(style, after.Italic).ApplyTo(style);
                }
            }
            prepared = WorkspaceDraftOperations.UpdateSubtitle(prepared, line.Id, value => value with { Style = style });
            foreach (var (property, field) in new[]
            {
                (AnimationProperty.STROKE_WIDTH, "StrokeWidth"),
                (AnimationProperty.LETTER_SPACING, "LetterSpacing"),
                (AnimationProperty.FILL_BLUR, "FillBlur"),
                (AnimationProperty.STROKE_BLUR, "StrokeBlur")
            })
            {
                if (fields.Contains(field) || fields.Contains(field + "Text"))
                {
                    var requested = StyleAnimationValue(prepared, prepared.Layers.Single(value => value.Id == primary.Id), property);
                    prepared = ApplyBatchStyleVisualValue(prepared, layer, property, requested);
                }
            }
            foreach (var (property, draft) in new[]
            {
                (AnimationProperty.FILL, vm.FillDraft), (AnimationProperty.STROKE, vm.StrokeDraft)
            })
            {
                if (draft.IsDirty)
                {
                    var value = StyleAnimationValue(prepared, layer, property);
                    prepared = ApplyBatchStyleVisualValue(prepared, layer, property, draft.ApplyEditsTo(value.Color));
                }
            }
        }
        return prepared;
    }

    private AnimationValue StyleAnimationValue(ProjectDocument document, ProjectLayer layer, AnimationProperty property)
    {
        var target = StyleAnimationTarget(layer);
        var identity = (target.Target ?? new(property)) with
        {
            Property = property, NodeId = null,
            State = AnimationPropertyMetadata.IsSubtitleVisualProperty(property)
                ? target.Target?.State ?? SubtitleAnimationState.NORMAL : SubtitleAnimationState.NORMAL
        };
        var track = layer.Tracks.FirstOrDefault(track => track.Target == identity);
        return track is not null ? SceneEvaluator.EvaluateTrack(track, target.LocalTime) :
            SubtitleAnimationEvaluation.GetBaseValue(layer, layer.SubtitleId is { } id
                ? document.Subtitles.Single(line => line.Id == id) : null, identity);
    }

    private ProjectDocument ApplyBatchStyleVisualValue(ProjectDocument document, ProjectLayer layer,
        AnimationProperty property, AnimationValue requested)
    {
        var target = StyleAnimationTarget(layer);
        var identity = (target.Target ?? new(property)) with { Property = property, NodeId = null };
        if (layer.Tracks.Any(track => track.Target == identity && !track.Transforms.IsEmpty))
        {
            throw new InvalidOperationException("Ordered transforms must be edited by their operation identity.");
        }
        return StyleAnimationValue(document, layer, property) == requested ? document :
            AnimationEditOperations.SetValue(document, target, property, requested);
    }

    private static SceneColor PatchStyleColor(ColorDraft draft, SceneColor before, SceneColor after, SceneColor target)
    {
        return draft.IsDirty ? draft.ApplyEditsTo(target) : before != after ? after : target;
    }
}
