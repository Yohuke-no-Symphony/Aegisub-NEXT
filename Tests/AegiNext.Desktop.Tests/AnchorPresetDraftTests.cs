using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Settings.Styles;

namespace AegiNext.Desktop.Tests;

public sealed class AnchorPresetDraftTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.5, 0)]
    [InlineData(1, 0)]
    [InlineData(0, 0.5)]
    [InlineData(0.5, 0.5)]
    [InlineData(1, 0.5)]
    [InlineData(0, 1)]
    [InlineData(0.5, 1)]
    [InlineData(1, 1)]
    public void EveryAnchorPreservesGlyphPositionAndPublishesOneCompleteChange(double x, double y)
    {
        var position = new SubtitlePosition
        {
            Anchor = new(0.25, 0.8), Pivot = new(0.6, 0.2), Offset = new(17.125, -22.5)
        };
        var geometry = CreateGeometry();
        var draft = new SubtitlePositionDraft();
        var changes = 0;
        draft.Changed += (_, _) =>
        {
            changes++;
            Assert.Null(draft.Validate());
            Assert.Equal(new ScenePoint(x, y), draft.CreatePosition()!.Anchor);
        };
        draft.Load(new() { Position = position }, geometry: geometry);
        Assert.Equal(0, changes);

        Assert.True(draft.SelectPreset(new(x, y)));

        Assert.Equal(1, changes);
        var next = draft.CreatePosition()!;
        Assert.Equal(position.Pivot, next.Pivot);
        AssertSameGlyphPoints(position, next, geometry);
    }

    [Fact]
    public void ShiftPreservesRotatedScaledGlyphsAndUsesInkSizeRatherThanParentSizeForPivot()
    {
        var position = new SubtitlePosition { Anchor = new(0.5, 1), Pivot = new(0.4, 0.9), Offset = new(8, -12) };
        var geometry = CreateGeometry();
        var draft = new SubtitlePositionDraft();
        draft.Load(new(), position, geometry: geometry);
        Assert.Null(draft.CreatePosition());

        Assert.True(draft.SelectPreset(new(1, 0), true));

        var next = draft.CreatePosition()!;
        Assert.Equal(new ScenePoint(1, 0), next.Anchor);
        Assert.Equal(next.Anchor, next.Pivot);
        AssertSameGlyphPoints(position, next, geometry);
    }

    [Theory]
    [InlineData(0.5, false)]
    [InlineData(2, false)]
    [InlineData(0.5, true)]
    [InlineData(2, true)]
    public void ScaledReferenceOffsetsPreserveActualGlyphsAcrossAnchorAndPivotChanges(double scale, bool setPivot)
    {
        var position = new SubtitlePosition { Anchor = new(0.25, 0.8), Pivot = new(0.6, 0.2), Offset = new(17, -22) };
        var geometry = CreateGeometry() with { OffsetScale = scale };
        var draft = new SubtitlePositionDraft();
        draft.Load(new() { Position = position }, geometry: geometry);

        Assert.True(draft.SelectPreset(new(1, 0), setPivot));

        AssertSameGlyphPoints(position, draft.CreatePosition()!, geometry);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AltResetsOffsetsAfterApplyingAnchorAndOptionalPivot(bool setPivot)
    {
        var position = new SubtitlePosition { Pivot = new(0.3, 0.7), Offset = new(71, -33) };
        var draft = new SubtitlePositionDraft();
        draft.Load(new() { Position = position }, geometry: CreateGeometry());

        Assert.True(draft.SelectPreset(new(0, 0.5), setPivot, true));

        var next = draft.CreatePosition()!;
        Assert.Equal(new ScenePoint(0, 0.5), next.Anchor);
        Assert.Equal(setPivot ? next.Anchor : position.Pivot, next.Pivot);
        Assert.Equal(new ScenePoint(), next.Offset);
    }

    [Fact]
    public void InvalidDraftAndMissingGeometryCannotBeOverwrittenByAPreset()
    {
        var draft = new SubtitlePositionDraft();
        draft.Load(new() { Position = new() }, geometry: CreateGeometry());
        draft.OffsetX.RawText = "7e-";
        var changes = 0;
        draft.Changed += (_, _) => changes++;

        Assert.False(draft.SelectPreset(new(0, 0), true, true));
        Assert.Equal("7e-", draft.OffsetX.RawText);
        Assert.Equal("OffsetXInput", draft.Validate());
        draft.UpdateGeometry(null);
        Assert.False(draft.CanSelectPreset);
        Assert.False(draft.SelectPreset(new(1, 1)));
        Assert.Equal("7e-", draft.OffsetX.RawText);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void TemplateMeasurementFeedsPresetsAndDoesNotReplaceTheEmbeddedFont()
    {
        var embedded = new EmbeddedSubtitleFont("sample.ttf", new('a', 64), [1, 2, 3]);
        var preset = new SubtitleStylePreset(Guid.NewGuid(), "Example", new(), embedded);
        var resolved = new SubtitlePosition { Anchor = new(0.5, 1), Pivot = new(0.5, 1), Offset = new(3, -11) };
        var geometry = CreateGeometry();
        var model = new StyleSettingsViewModel();
        var requests = 0;
        model.SetPositionMeasurement(value =>
        {
            requests++;
            Assert.Same(embedded, value.Font);
            return new(value.Style.Position ?? resolved, geometry);
        });
        model.UpdateStyles([preset]);
        Assert.True(model.Position.CanSelectPreset);

        Assert.True(model.Position.SelectPreset(new(0, 0), true));

        Assert.True(requests >= 2);
        Assert.Same(embedded, model.Draft!.Font);
        AssertSameGlyphPoints(resolved, model.Draft.Style.Position!, geometry);
        model.Position.OffsetX.RawText = "7e-";
        model.RefreshLanguage();
        Assert.Equal("7e-", model.Position.OffsetX.RawText);
        Assert.Same(geometry, model.Position.Geometry);
    }

    private static LayerPositionDraftGeometry CreateGeometry()
    {
        return new(new(1920, 1080), new(417.5, 862), new(231.25, 46.5),
            new(X: 19, Y: -7, ScaleX: 1.75, ScaleY: 0.6, Rotation: 37, AnchorX: 4, AnchorY: -2));
    }

    private static void AssertSameGlyphPoints(SubtitlePosition previous, SubtitlePosition next, LayerPositionDraftGeometry geometry)
    {
        foreach (var normalized in new ScenePoint[] { new(0, 0), new(1, 0), new(1, 1), new(0, 1), new(0.3, 0.7) })
        {
            var before = Map(previous, normalized, geometry);
            var after = Map(next, normalized, geometry);
            Assert.Equal(before.X, after.X, 9);
            Assert.Equal(before.Y, after.Y, 9);
        }
    }

    private static ScenePoint Map(SubtitlePosition position, ScenePoint point, LayerPositionDraftGeometry geometry)
    {
        var transform = geometry.Transform;
        var x = ((point.X - position.Pivot.X) * geometry.GlyphSize.X - transform.AnchorX) * transform.ScaleX;
        var y = ((point.Y - position.Pivot.Y) * geometry.GlyphSize.Y - transform.AnchorY) * transform.ScaleY;
        var radians = transform.Rotation * Math.PI / 180;
        var localX = Math.Cos(radians) * x - Math.Sin(radians) * y +
                     position.Anchor.X * geometry.ParentSize.X + position.Offset.X * geometry.OffsetScale + transform.X;
        var localY = Math.Sin(radians) * x + Math.Cos(radians) * y +
                     position.Anchor.Y * geometry.ParentSize.Y + position.Offset.Y * geometry.OffsetScale + transform.Y;
        return new(localX * 0.75 + localY * -0.2 + 41, localX * 0.3 + localY * 1.2 - 17);
    }
}
