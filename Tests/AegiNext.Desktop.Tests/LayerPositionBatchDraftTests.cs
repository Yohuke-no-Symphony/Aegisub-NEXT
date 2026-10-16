using System.Globalization;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;

namespace AegiNext.Desktop.Tests;

public sealed class LayerPositionBatchDraftTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PivotEditsPreserveEachTargetsProjectionAndUntouchedComponents(bool spatial)
    {
        var source = Source();
        var position = TargetPosition();
        var geometry = TargetGeometry(spatial);
        var target = new LayerPositionDraft();
        target.Load(position, geometry);

        source.PivotX.RawText = "0.8";
        source.ApplyEditsTo(target);

        var changed = target.CreatePosition()!;
        Assert.True(source.HasEdits);
        Assert.Equal(position.Anchor, changed.Anchor);
        Assert.Equal(new ScenePoint(0.8, position.Pivot.Y), changed.Pivot);
        AssertSameProjection(position, changed, geometry, target.PrepareTransform(geometry.Transform));
        if (spatial)
        {
            Assert.Equal(position.Offset, changed.Offset);
        }
        else
        {
            Assert.NotEqual(source.CreatePosition()!.Offset, changed.Offset);
        }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    public void PresetsUseEachTargetsGeometryAndCanPromoteAutomaticSubtitlePositions(bool setPivot, bool resetOffset, bool automatic)
    {
        var source = Source();
        var position = new SubtitlePosition
        {
            Anchor = TargetPosition().Anchor, Pivot = TargetPosition().Pivot, Offset = TargetPosition().Offset
        };
        var geometry = TargetGeometry();
        var target = new SubtitlePositionDraft();
        target.Load(new() { Position = automatic ? null : position }, position, geometry: geometry);
        Assert.True(source.SelectPreset(new(1, 0), setPivot, resetOffset));

        source.ApplyEditsTo(target);

        var changed = target.CreatePosition()!;
        Assert.True(target.IsExplicit);
        Assert.Equal(new ScenePoint(1, 0), changed.Anchor);
        Assert.Equal(setPivot ? changed.Anchor : position.Pivot, changed.Pivot);
        if (resetOffset)
        {
            Assert.Equal(new ScenePoint(), changed.Offset);
        }
        else
        {
            AssertSameProjection(position, changed, geometry, target.PrepareTransform(geometry.Transform));
        }
    }

    [Fact]
    public void NumericEditsAfterAPresetKeepTheirOrderAndUseTheTargetsPivotCompensation()
    {
        var source = Source();
        var position = TargetPosition();
        var geometry = TargetGeometry();
        var target = new LayerPositionDraft();
        target.Load(position, geometry);
        var anchor = new ScenePoint(0.5, 0);
        Assert.True(source.SelectPreset(anchor, setPivot: true));
        source.OffsetX.RawText = "50";
        source.PivotY.RawText = "0.6";

        source.ApplyEditsTo(target);

        var changed = target.CreatePosition()!;
        var presetDelta = geometry.PivotDisplacement(position.Pivot, anchor);
        var finalDelta = geometry.PivotDisplacement(anchor, new(anchor.X, 0.6));
        var presetOffsetY = position.Offset.Y + (position.Anchor.Y - anchor.Y) * geometry.ParentSize.Y / geometry.OffsetScale + presetDelta.Y;
        Assert.Equal(anchor, changed.Anchor);
        Assert.Equal(new ScenePoint(anchor.X, 0.6), changed.Pivot);
        Assert.Equal(50 + finalDelta.X, changed.Offset.X, 8);
        Assert.Equal(presetOffsetY + finalDelta.Y, changed.Offset.Y, 8);
    }

    [Fact]
    public void MultiplePresetsRetainExplicitOffsetsBetweenThemWithoutCopyingParentDimensions()
    {
        var source = Source();
        var position = TargetPosition();
        var geometry = TargetGeometry();
        var target = new LayerPositionDraft();
        target.Load(position, geometry);
        var firstAnchor = new ScenePoint(0, 1);
        var secondAnchor = new ScenePoint(1, 0);
        Assert.True(source.SelectPreset(firstAnchor, setPivot: true, resetOffset: true));
        source.OffsetX.RawText = "45";
        Assert.True(source.SelectPreset(secondAnchor));
        source.OffsetY.RawText = "27";

        source.ApplyEditsTo(target);

        var changed = target.CreatePosition()!;
        Assert.Equal(secondAnchor, changed.Anchor);
        Assert.Equal(firstAnchor, changed.Pivot);
        Assert.Equal(45 - geometry.ParentSize.X / geometry.OffsetScale, changed.Offset.X, 8);
        Assert.Equal(27, changed.Offset.Y);
    }

    [Fact]
    public void RestoringPivotCancelsItsFinalValueAndPreservesTheOtherExplicitOffsetIntent()
    {
        var source = Source();
        var position = TargetPosition();
        var geometry = TargetGeometry();
        var target = new LayerPositionDraft();
        target.Load(position, geometry);
        source.PivotX.RawText = "0.8";
        source.OffsetX.RawText = "50";
        source.PivotX.RawText = "0.9";
        Assert.True(source.RestoreField("PivotXInput"));

        source.ApplyEditsTo(target);

        var changed = target.CreatePosition()!;
        var compensation = geometry.PivotDisplacement(new(0.8, position.Pivot.Y), position.Pivot);
        Assert.True(source.HasEdits);
        Assert.Equal(position.Pivot, changed.Pivot);
        Assert.Equal(position.Anchor, changed.Anchor);
        Assert.Equal(50 + compensation.X, changed.Offset.X, 8);
        Assert.Equal(position.Offset.Y, changed.Offset.Y, 8);
    }

    [Fact]
    public void RestoringTheOnlyEditedFieldLeavesNoPositionIntentForOtherTargets()
    {
        var source = Source();
        var position = TargetPosition();
        var target = new LayerPositionDraft();
        target.Load(position, TargetGeometry());
        source.PivotX.RawText = "0.8";
        Assert.True(source.RestoreField("PivotXInput"));

        source.ApplyEditsTo(target);

        Assert.False(source.HasEdits);
        Assert.Equal(position, target.CreatePosition());
    }

    [Fact]
    public void RepeatedNumericInputReplaysOneFinalEditAndPreservesTheUntouchedAxis()
    {
        var source = Source();
        var position = TargetPosition();
        var target = new LayerPositionDraft();
        target.Load(position, TargetGeometry());
        for (var value = 1; value <= 1000; value++)
        {
            source.OffsetX.RawText = value.ToString(CultureInfo.InvariantCulture);
        }
        var changes = 0;
        target.Changed += (_, _) => changes++;

        source.ApplyEditsTo(target);

        Assert.Equal(1, changes);
        Assert.Equal(position with { Offset = new(1000, position.Offset.Y) }, target.CreatePosition());
    }

    [Fact]
    public void UnchangedPrimaryValueCanStillSetTheSameComponentOnOtherTargets()
    {
        var source = Source();
        var position = TargetPosition();
        var target = new LayerPositionDraft();
        target.Load(position, TargetGeometry());
        source.OffsetX.RawText = "15.0";

        source.ApplyEditsTo(target);

        Assert.True(source.HasEdits);
        Assert.Equal(position with { Offset = new(15, position.Offset.Y) }, target.CreatePosition());
    }

    [Fact]
    public void LoadingOrClearingEditsDiscardsPendingOperationsWithoutChangingOtherTargets()
    {
        var source = Source();
        var position = TargetPosition();
        var target = new LayerPositionDraft();
        target.Load(position, TargetGeometry());
        source.OffsetX.RawText = "17";
        source.ClearEdits();
        Assert.False(source.HasEdits);
        source.ApplyEditsTo(target);
        Assert.Equal(position, target.CreatePosition());
        Assert.True(source.SelectPreset(new(1, 0), setPivot: true));
        source.Load(new(), TargetGeometry());

        source.ApplyEditsTo(target);

        Assert.False(source.HasEdits);
        Assert.Equal(position, target.CreatePosition());
    }

    [Fact]
    public void AutomaticModeDiscardsHiddenInvalidInputsAndAppliesTheModeToEveryTarget()
    {
        var source = new SubtitlePositionDraft();
        source.Load(new() { Position = new() }, geometry: TargetGeometry());
        var target = new SubtitlePositionDraft();
        target.Load(new() { Position = new() }, geometry: TargetGeometry());
        source.OffsetX.RawText = "invalid";
        source.IsExplicit = false;

        source.ApplyEditsTo(target);

        Assert.True(source.HasEdits);
        Assert.Null(source.Validate());
        Assert.False(target.IsExplicit);
        Assert.Null(target.CreatePosition());
    }

    [Fact]
    public void InvalidSourceCannotPartiallyChangeTheTarget()
    {
        var source = Source();
        var position = TargetPosition();
        var target = new LayerPositionDraft();
        target.Load(position, TargetGeometry());
        source.OffsetX.RawText = "17";
        source.PivotX.RawText = "invalid";

        Assert.Throws<InvalidDataException>(() => source.ApplyEditsTo(target));

        Assert.Equal(position, target.CreatePosition());
        Assert.False(target.HasEdits);
    }

    [Fact]
    public void GestureSnapshotRestoresInvalidRawTextAndItsPreviousProjectionWithoutLosingOtherDrafts()
    {
        var source = Source();
        source.AnchorY.RawText = "0.6";
        source.OffsetY.RawText = "7e-";
        var restore = source.CaptureEditState();
        source.OffsetY.RawText = "17";
        source.PivotX.RawText = "0.8";
        source.OffsetX.RawText = "50";
        var changes = 0;
        source.Changed += (_, _) => changes++;

        restore();

        Assert.Equal(1, changes);
        Assert.Equal("0.6", source.AnchorY.RawText);
        Assert.Equal("7e-", source.OffsetY.RawText);
        Assert.Equal(20m, source.OffsetY.Value);
        Assert.Equal("OffsetYInput", source.Validate());
        Assert.True(source.RestoreField("OffsetYInput"));
        var position = TargetPosition();
        var target = new LayerPositionDraft();
        target.Load(position, TargetGeometry());
        source.ApplyEditsTo(target);
        Assert.Equal(position with { Anchor = position.Anchor with { Y = 0.6 } }, target.CreatePosition());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GestureSnapshotRestoresThePreviousPivotCompensationAndPendingIntent(bool spatial)
    {
        var source = new LayerPositionDraft();
        var sourcePosition = TargetPosition();
        var sourceGeometry = TargetGeometry(spatial);
        source.Load(sourcePosition, sourceGeometry);
        source.PivotX.RawText = "0.6";
        var previous = source.CreatePosition();
        var previousTransform = source.PrepareTransform(sourceGeometry.Transform);
        var restore = source.CaptureEditState();
        Assert.True(source.SelectPreset(new(1, 0), setPivot: true, resetOffset: true));
        source.OffsetX.RawText = "50";
        source.PivotY.RawText = "0.9";
        source.IsExplicit = false;

        restore();

        Assert.True(source.IsExplicit);
        Assert.True(source.HasEdits);
        Assert.Equal(previous, source.CreatePosition());
        Assert.Equal(previousTransform, source.PrepareTransform(sourceGeometry.Transform));
        var targetPosition = new LayerPosition { Anchor = new(0.2, 0.8), Pivot = new(0.1, 0.4), Offset = new(5, 9) };
        var targetGeometry = sourceGeometry with { GlyphSize = new(210, 37) };
        var target = new LayerPositionDraft();
        target.Load(targetPosition, targetGeometry);
        source.ApplyEditsTo(target);
        var changed = target.CreatePosition()!;
        Assert.Equal(new ScenePoint(0.6, targetPosition.Pivot.Y), changed.Pivot);
        Assert.Equal(targetPosition.Anchor, changed.Anchor);
        AssertSameProjection(targetPosition, changed, targetGeometry, target.PrepareTransform(targetGeometry.Transform));
    }

    [Fact]
    public void CleanGestureSnapshotDiscardsCanceledPresetsAndEveryNumericIntent()
    {
        var source = Source();
        var previous = source.CreatePosition();
        var restore = source.CaptureEditState();
        Assert.False(source.HasEdits);
        Assert.True(source.SelectPreset(new(1, 0), setPivot: true, resetOffset: true));
        source.OffsetX.RawText = "50";
        source.PivotY.RawText = "0.9";

        restore();

        Assert.False(source.HasEdits);
        Assert.Equal(previous, source.CreatePosition());
        var targetPosition = TargetPosition();
        var target = new LayerPositionDraft();
        target.Load(targetPosition, TargetGeometry());
        source.ApplyEditsTo(target);
        Assert.Equal(targetPosition, target.CreatePosition());
    }

    [Fact]
    public void GestureSnapshotCanRestoreAllSixRawFieldsAndReplayOnlyTheOriginalOperations()
    {
        var source = Source();
        source.AnchorX.RawText = "0.2";
        source.AnchorY.RawText = "0.6";
        source.PivotX.RawText = "0.6";
        source.PivotY.RawText = "0.8";
        source.OffsetX.RawText = "17";
        source.OffsetY.RawText = "-21";
        var fields = new[] { source.AnchorX, source.AnchorY, source.PivotX, source.PivotY, source.OffsetX, source.OffsetY };
        var previousText = fields.Select(field => field.RawText).ToArray();
        var previous = source.CreatePosition();
        var restore = source.CaptureEditState();
        foreach (var field in fields)
        {
            field.RawText = "0.5";
        }

        restore();

        Assert.Equal(previousText, fields.Select(field => field.RawText));
        Assert.Equal(previous, source.CreatePosition());
        var target = new LayerPositionDraft();
        target.Load(TargetPosition(), TargetGeometry());
        source.ApplyEditsTo(target);
        Assert.Equal(previous, target.CreatePosition());
    }

    private static LayerPositionDraft Source()
    {
        var source = new LayerPositionDraft();
        source.Load(new() { Anchor = new(0.25, 0.75), Pivot = new(0.2, 0.4), Offset = new(15, 20) },
            new(new(800, 600), new(), new(100, 50), new(ScaleX: 2, ScaleY: 3, Rotation: 90)));
        return source;
    }

    private static LayerPosition TargetPosition() => new()
    {
        Anchor = new(0.75, 0.5), Pivot = new(0.3, 0.7), Offset = new(-12, 30)
    };

    private static LayerPositionDraftGeometry TargetGeometry(bool spatial = false) =>
        new(new(1280, 720), new(47, 83), new(560, 90), new(19, -7, 1.75, 0.6, 37, 4, -2)
        {
            RotationX = spatial ? 31 : 0, RotationY = spatial ? -24 : 0,
            PerspectiveDistance = 420, ProjectionTranslation = new(11, -8, spatial ? 19 : 0)
        }) { OffsetScale = 0.75 };

    private static void AssertSameProjection(LayerPosition original, LayerPosition changed,
        LayerPositionDraftGeometry geometry, LayerTransform changedTransform)
    {
        var before = Matrix(original, geometry, geometry.Transform);
        var after = Matrix(changed, geometry, changedTransform);
        foreach (var point in new ScenePoint[] { geometry.GlyphOrigin, new(607, 173), new(57, 112) })
        {
            var previous = before.Map(point);
            var next = after.Map(point);
            Assert.Equal(previous.X, next.X, 8);
            Assert.Equal(previous.Y, next.Y, 8);
        }
    }

    private static ProjectionMatrix3x3 Matrix(LayerPosition position, LayerPositionDraftGeometry geometry, LayerTransform transform)
    {
        var pivot = LayerPositionGeometry.LocalPivot(position, geometry.GlyphOrigin, geometry.GlyphSize);
        var center = new ScenePoint(position.Anchor.X * geometry.ParentSize.X + position.Offset.X * geometry.OffsetScale + transform.X,
            position.Anchor.Y * geometry.ParentSize.Y + position.Offset.Y * geometry.OffsetScale + transform.Y);
        return SceneProjection.CreateMatrix(transform, new(pivot.X + transform.Pivot.X, pivot.Y + transform.Pivot.Y), center);
    }
}
