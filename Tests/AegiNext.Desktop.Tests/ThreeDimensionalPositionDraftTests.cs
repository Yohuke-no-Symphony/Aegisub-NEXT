using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;

namespace AegiNext.Desktop.Tests;

public sealed class ThreeDimensionalPositionDraftTests
{
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 0.5)]
    [InlineData(true, 2)]
    public void PivotChangesPreserveTheCompleteProjectionWithoutChangingReferenceOffsets(bool preset, double offsetScale)
    {
        var position = new LayerPosition { Anchor = new(0.25, 0.75), Pivot = new(0.1, 0.8), Offset = new(15, -20) };
        var transform = new LayerTransform(19, -7, 1.75, 0.6, 37, 4, -2)
        {
            RotationX = 31, RotationY = -24, PerspectiveDistance = 420,
            ProjectionTranslation = new(11, -8, 19)
        };
        var geometry = new LayerPositionDraftGeometry(new(1920, 1080), new(417.5, 862), new(231.25, 46.5), transform)
        {
            OffsetScale = offsetScale
        };
        var draft = new LayerPositionDraft();
        draft.Load(position, geometry);

        if (preset)
        {
            Assert.True(draft.SelectPreset(new(1, 0), setPivot: true));
        }
        else
        {
            draft.PivotX.RawText = "1";
            draft.PivotY.RawText = "0";
        }

        var next = draft.CreatePosition()!;
        var prepared = draft.PrepareTransform(transform);
        Assert.Equal(prepared, draft.PrepareTransform(transform));
        Assert.Equal(prepared, draft.PrepareTransform(prepared));
        Assert.Equal(new ScenePoint(1, 0), next.Pivot);
        Assert.Equal(transform.Position, prepared.Position);
        Assert.Equal(transform.Scale, prepared.Scale);
        Assert.Equal(transform.RotationX, prepared.RotationX);
        Assert.Equal(transform.RotationY, prepared.RotationY);
        var originalCenter = Center(position, geometry);
        var nextCenter = Center(next, geometry);
        Assert.Equal(originalCenter.X, nextCenter.X, 9);
        Assert.Equal(originalCenter.Y, nextCenter.Y, 9);
        var before = SceneProjection.CreateMatrix(transform, Pivot(position, geometry), originalCenter);
        var after = SceneProjection.CreateMatrix(prepared, Pivot(next, geometry), nextCenter);
        foreach (var point in new ScenePoint[] { new(0, 0), new(1, 0), new(1, 1), new(0, 1), new(0.3, 0.7) })
        {
            var local = new ScenePoint(geometry.GlyphOrigin.X + point.X * geometry.GlyphSize.X,
                geometry.GlyphOrigin.Y + point.Y * geometry.GlyphSize.Y);
            var previous = before.Map(local);
            var changed = after.Map(local);
            Assert.Equal(previous.X, changed.X, 8);
            Assert.Equal(previous.Y, changed.Y, 8);
        }
    }

    [Fact]
    public void RestoringOnePivotComponentRestoresItsProjectionCompensationAndKeepsOtherDrafts()
    {
        var transform = new LayerTransform { RotationX = 30, RotationY = 20 };
        var position = new LayerPosition { Pivot = new(0.2, 0.4), Offset = new(15, 20) };
        var draft = new LayerPositionDraft();
        draft.Load(position, new(new(800, 600), new(), new(100, 50), transform));
        draft.PivotX.RawText = "0.8";
        draft.PivotY.RawText = "0.6";
        draft.OffsetY.RawText = "21";

        Assert.True(draft.RestoreField("PivotXInput"));

        var delta = SceneProjection.RotateScaled(new(0, 10), transform);
        var translation = draft.PrepareTransform(transform).ProjectionTranslation;
        Assert.Equal(delta.X, translation.X, 8);
        Assert.Equal(delta.Y, translation.Y, 8);
        Assert.Equal(delta.Z, translation.Z, 8);
        Assert.Equal("0.6", draft.PivotY.RawText);
        Assert.Equal("21", draft.OffsetY.RawText);
        draft.PivotY.RawText = "7e-";
        Assert.Equal("PivotYInput", draft.Validate());
        Assert.Throws<InvalidDataException>(() => draft.PrepareTransform(transform));
    }

    [Fact]
    public void DirectAffinePivotTextEditsUseOffsetCompensationAndEscapeReversesOnlyThatComponent()
    {
        var transform = new LayerTransform(ScaleX: 2, ScaleY: 3, Rotation: 90);
        var position = new LayerPosition { Pivot = new(0.2, 0.4), Offset = new(15, 20) };
        var draft = new LayerPositionDraft();
        draft.Load(position, new(new(800, 600), new(), new(100, 50), transform));

        draft.PivotX.RawText = "0.8";
        draft.PivotY.RawText = "0.6";

        Assert.Equal(-15, draft.CreatePosition()!.Offset.X, 8);
        Assert.Equal(140, draft.CreatePosition()!.Offset.Y, 8);
        Assert.Equal(transform, draft.PrepareTransform(transform));
        Assert.True(draft.RestoreField("PivotXInput"));
        Assert.Equal(-15, draft.CreatePosition()!.Offset.X, 8);
        Assert.Equal(20, draft.CreatePosition()!.Offset.Y, 8);
        Assert.Equal("0.6", draft.PivotY.RawText);
    }

    private static ScenePoint Center(LayerPosition position, LayerPositionDraftGeometry geometry)
    {
        return new(position.Anchor.X * geometry.ParentSize.X + position.Offset.X * geometry.OffsetScale + geometry.Transform.X,
            position.Anchor.Y * geometry.ParentSize.Y + position.Offset.Y * geometry.OffsetScale + geometry.Transform.Y);
    }

    private static ScenePoint Pivot(LayerPosition position, LayerPositionDraftGeometry geometry)
    {
        var pivot = LayerPositionGeometry.LocalPivot(position, geometry.GlyphOrigin, geometry.GlyphSize);
        return new(pivot.X + geometry.Transform.Pivot.X, pivot.Y + geometry.Transform.Pivot.Y);
    }
}
