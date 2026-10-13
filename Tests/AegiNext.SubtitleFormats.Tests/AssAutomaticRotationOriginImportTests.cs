using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.SubtitleFormats;
using AegiNext.Testing.SubtitleFormats;

namespace AegiNext.SubtitleFormats.Tests;

public sealed class AssAutomaticRotationOriginImportTests
{
    [Fact]
    public void FixedAutomaticLayoutUsesActualMeasuredBaseWithoutFreezingStylePlacement()
    {
        var measurer = new RecordingSubtitlePlacementMeasurer(new(new(120, 90), new(30, 50), new(10, 20), new(40, 30)));
        var imported = AssSubtitleFormat.Parse(AssBoundarySource.File(@"{\org(180,130)\fscx200\fscy50\frz30}Fj"),
            640, 360, placementMeasurer: measurer);
        var clip = Assert.Single(imported.Clips);

        Assert.Equal(new ScenePoint(60, 40), clip.Transform.Position);
        Assert.Equal(new ScenePoint(30, 80), clip.Transform.Pivot);
        Assert.Null(clip.Line.Style.Position);
        Assert.Equal(640, measurer.Document!.Width);
        Assert.Equal(360, measurer.Document.Height);
        ProjectValidator.Validate(measurer.Document);
        Assert.Equal(clip.Line.Id, Assert.Single(measurer.Document.Layers).SubtitleId);
        Assert.Equal("Fj", measurer.Line!.Text);
        Assert.DoesNotContain(imported.Diagnostics, diagnostic => diagnostic.Code == "Ass.RotationOrigin");
    }

    [Fact]
    public void SourceResolutionApiPassesItsDeclaredCanvasToTheMeasurer()
    {
        var measurer = new RecordingSubtitlePlacementMeasurer(new(new(120, 90), default, default, new(40, 30)));
        var imported = AssSubtitleFormat.ParseAtSourceResolution(AssBoundarySource.File(@"{\org(180,130)}Fj"),
            placementMeasurer: measurer);

        Assert.Equal(imported.Width, measurer.Document!.Width);
        Assert.Equal(imported.Height, measurer.Document.Height);
        Assert.Equal(new ScenePoint(60, 40), Assert.Single(imported.Result.Clips).Transform.Position);
    }

    [Fact]
    public void MeasurementReceivesFinalInlineTypographyAndDialogueMargins()
    {
        var measurer = new RecordingSubtitlePlacementMeasurer(new(new(120, 90), default, default, new(40, 30)));
        var source = AssBoundarySource.File(@"{\q2\org(180,130)\fs30\fsp4}F{\fnNoto Serif\fs40}j")
            .Replace("Default,,0,0,0,,", "Default,,35,45,55,,", StringComparison.Ordinal);
        var imported = AssSubtitleFormat.Parse(source, 1280, 720, placementMeasurer: measurer);
        var measured = measurer.Line!;

        Assert.Equal(new SubtitleMargins(70, 90, 110), measured.Style.Margins);
        Assert.Equal(SubtitleWrapMode.NO_WRAP, measured.Style.WrapMode);
        Assert.Equal(60, At(measured, 0).FontSize);
        Assert.Equal(8, At(measured, 0).LetterSpacing);
        Assert.Equal(80, At(measured, 1).FontSize);
        Assert.Equal("Noto Serif", At(measured, 1).FontFamily);
        Assert.DoesNotContain(imported.Diagnostics, diagnostic => diagnostic.Code == "Ass.RotationOrigin");
    }

    [Theory]
    [InlineData(@"\fs40")]
    [InlineData(@"\fsp6")]
    public void ChangingTypographyRemainsDiagnosedAndIsNotMeasuredAsAStaticLayout(string tag)
    {
        var measurer = new RecordingSubtitlePlacementMeasurer(new(new(120, 90), default, default, new(40, 30)));
        var imported = AssSubtitleFormat.Parse(AssBoundarySource.File(@"{\org(180,130)\t(0,2000," + tag + ")}Fj"),
            640, 360, placementMeasurer: measurer);

        Assert.Null(measurer.Document);
        Assert.Contains(imported.Diagnostics, diagnostic => diagnostic.Code == "Ass.RotationOrigin" &&
            diagnostic.Message.Contains("font-size or letter-spacing", StringComparison.Ordinal));
        Assert.Contains(Assert.Single(imported.Clips).Tracks, track => track.Property is AnimationProperty.FONT_SIZE or AnimationProperty.LETTER_SPACING);
    }

    [Fact]
    public void CompletedTypographyOperationsAreMeasuredAtTheirVisibleWindowConstants()
    {
        var measurer = new RecordingSubtitlePlacementMeasurer(new(new(120, 90), default, default, new(40, 30)));
        var imported = AssSubtitleFormat.Parse(AssBoundarySource.File(@"{\org(180,130)\t(-1000,-500,\fs40\fsp6)}Fj"),
            640, 360, placementMeasurer: measurer);

        Assert.NotNull(measurer.Line);
        Assert.Equal(40, At(measurer.Line, 0).FontSize);
        Assert.Equal(6, At(measurer.Line, 0).LetterSpacing);
        Assert.DoesNotContain(imported.Diagnostics, diagnostic => diagnostic.Code == "Ass.RotationOrigin");
    }

    [Fact]
    public void MissingMeasurementsKeepAnExplicitDiagnosticAndPreserveOtherGeometry()
    {
        var imported = AssSubtitleFormat.Parse(AssBoundarySource.File(@"{\org(180,130)\frz30}Fj"), 640, 360);

        Assert.Contains(imported.Diagnostics, diagnostic => diagnostic.Code == "Ass.RotationOrigin" &&
            diagnostic.Message.Contains("actual font layout measurements", StringComparison.Ordinal));
        Assert.Equal(-30, Assert.Single(imported.Clips).Transform.Rotation);
        Assert.Equal(default, Assert.Single(imported.Clips).Transform.Pivot);
    }

    [Fact]
    public void ExplicitPositionDoesNotInvokeTheAutomaticPlacementMeasurer()
    {
        var measurer = new RecordingSubtitlePlacementMeasurer(new(new(120, 90), default, default, new(40, 30)));
        var imported = AssSubtitleFormat.Parse(AssBoundarySource.File(@"{\pos(100,80)\org(180,130)}Fj"),
            640, 360, placementMeasurer: measurer);

        Assert.Null(measurer.Document);
        Assert.Equal(new ScenePoint(80, 50), Assert.Single(imported.Clips).Transform.Pivot);
        Assert.DoesNotContain(imported.Diagnostics, diagnostic => diagnostic.Code == "Ass.RotationOrigin");
    }

    [Fact]
    public void InvalidMeasuredCoordinatesDoNotCreateInvalidNativeTransforms()
    {
        var measurer = new RecordingSubtitlePlacementMeasurer(new(new(double.NaN, 90), default, default, new(40, 30)));
        var imported = AssSubtitleFormat.Parse(AssBoundarySource.File(@"{\org(180,130)}Fj"),
            640, 360, placementMeasurer: measurer);

        Assert.Equal(new LayerTransform(), Assert.Single(imported.Clips).Transform);
        Assert.Contains(imported.Diagnostics, diagnostic => diagnostic.Code == "Ass.RotationOrigin");
    }

    private static SubtitleStyle At(SubtitleLine line, int offset)
    {
        return line.InlineSpans.FirstOrDefault(span => span.Utf16Start <= offset && offset < span.Utf16Start + span.Utf16Length)
            ?.Style.ApplyTo(line.Style) ?? line.Style;
    }
}
