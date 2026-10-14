using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;

namespace AegiNext.Desktop.Tests;

public sealed class LayerPositionDraftPivotRangeTests
{
    [Theory]
    [InlineData("-0.1")]
    [InlineData("1.1")]
    public void OrdinaryPositionRejectsOutsidePivotWithoutCompensatingOffsets(string rawText)
    {
        var draft = new LayerPositionDraft();
        var position = CreatePosition();
        draft.Load(position, CreateGeometry());

        draft.PivotX.RawText = rawText;

        Assert.Equal(0m, draft.PivotMinimum);
        Assert.Equal(1m, draft.PivotMaximum);
        Assert.Equal("PivotXInput", draft.Validate());
        Assert.Equal(position.Offset.X, (double)draft.OffsetX.Parse()!);
        Assert.Equal(position.Offset.Y, (double)draft.OffsetY.Parse()!);
        Assert.False(draft.SelectPreset(new(1, 1), setPivot: true));
        Assert.Equal(rawText, draft.PivotX.RawText);
    }

    [Fact]
    public void ExtendedPivotRangePreservesProjectionAndEscapeReversesItsCompensation()
    {
        var draft = new LayerPositionDraft { PivotMinimum = -4000000000m, PivotMaximum = 4000000000m };
        var position = CreatePosition();
        var geometry = CreateGeometry();
        draft.Load(position, geometry);

        draft.PivotX.RawText = "-2";
        draft.PivotY.RawText = "3";

        Assert.Null(draft.Validate());
        var changed = draft.CreatePosition()!;
        Assert.Equal(new ScenePoint(-2, 3), changed.Pivot);
        AssertSameProjection(position, changed, geometry);
        Assert.True(draft.RestoreField("PivotXInput"));
        Assert.Equal("3", draft.PivotY.RawText);
        AssertSameProjection(position, draft.CreatePosition()!, geometry);
        draft.PivotX.RawText = "-4000000001";
        Assert.Equal("PivotXInput", draft.Validate());
    }

    [Fact]
    public void PivotWhoseCompensationExceedsOffsetRangeStaysEditableAndCannotCommitAnUncompensatedPlacement()
    {
        var draft = new LayerPositionDraft { PivotMinimum = -4000000000m, PivotMaximum = 4000000000m };
        draft.Load(new() { Pivot = new(0.5, 0.5) }, new(new(800, 600), new(), new(100, 100), new()));

        draft.PivotX.RawText = "20000000";

        Assert.Equal("20000000", draft.PivotX.RawText);
        Assert.Equal("OffsetXInput", draft.Validate());
        Assert.Equal(1999999950m, draft.OffsetX.Parse());
        Assert.Throws<InvalidDataException>(() => draft.CreatePosition());
        Assert.True(draft.RestoreField("PivotXInput"));
        Assert.Null(draft.Validate());
        Assert.Equal(new ScenePoint(0.5, 0.5), draft.CreatePosition()!.Pivot);
    }

    [Fact]
    public void ShiftPresetFromAnOutsidePivotPreservesPlacementAndPublishesOneChange()
    {
        var draft = new LayerPositionDraft { PivotMinimum = -4000000000m, PivotMaximum = 4000000000m };
        var position = CreatePosition() with { Pivot = new(-2, 3) };
        var geometry = CreateGeometry();
        draft.Load(position, geometry);
        var changes = 0;
        draft.Changed += (_, _) => changes++;

        Assert.True(draft.SelectPreset(new(1, 0), setPivot: true));

        Assert.Equal(1, changes);
        Assert.Equal(new ScenePoint(1, 0), draft.CreatePosition()!.Pivot);
        AssertSameProjection(position, draft.CreatePosition()!, geometry);
    }

    private static LayerPosition CreatePosition() => new()
    {
        Anchor = new(0.25, 0.75), Pivot = new(0.2, 0.4), Offset = new(15, -20)
    };

    private static LayerPositionDraftGeometry CreateGeometry() =>
        new(new(800, 600), new(37, 82), new(100, 50), new(19, -7, 2, 3, 37));

    private static void AssertSameProjection(LayerPosition original, LayerPosition changed, LayerPositionDraftGeometry geometry)
    {
        var before = Matrix(original, geometry);
        var after = Matrix(changed, geometry);
        foreach (var point in new ScenePoint[] { geometry.GlyphOrigin, new(137, 132), new(57, 112) })
        {
            var previous = before.Map(point);
            var next = after.Map(point);
            Assert.Equal(previous.X, next.X, 8);
            Assert.Equal(previous.Y, next.Y, 8);
        }
    }

    private static ProjectionMatrix3x3 Matrix(LayerPosition position, LayerPositionDraftGeometry geometry)
    {
        var pivot = LayerPositionGeometry.LocalPivot(position, geometry.GlyphOrigin, geometry.GlyphSize);
        var center = LayerPositionGeometry.BasePosition(position, geometry.ParentSize);
        return SceneProjection.CreateMatrix(geometry.Transform, pivot,
            new(center.X + geometry.Transform.X, center.Y + geometry.Transform.Y));
    }
}
