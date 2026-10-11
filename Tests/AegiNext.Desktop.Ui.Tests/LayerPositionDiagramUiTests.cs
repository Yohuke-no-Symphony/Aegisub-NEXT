using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Editing;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkiaSharp;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class LayerPositionDiagramUiTests
{
    [AvaloniaFact]
    public void SharedVectorRowsKeepValidationFocusAndEscapeRestoresOnlyFocusedComponent()
    {
        using var environment = new UiTestEnvironment();
        var draft = new SubtitlePositionDraft();
        draft.Load(new() { Position = CreatePosition() }, geometry: CreateGeometry());
        var editor = new LayerPositionEditor { DataContext = draft };
        var window = new Window { Width = 440, Height = 760, Content = editor };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Assert.Equal(3, editor.GetLogicalDescendants().OfType<VectorDraftInput>().Count());
            var inputs = editor.GetLogicalDescendants().OfType<NumericDraftInput>().ToArray();
            var anchor = Assert.Single(inputs, input => input.Name == "AnchorXInput");
            var pivot = Assert.Single(inputs, input => input.Name == "PivotYInput");
            var offset = Assert.Single(inputs, input => input.Name == "OffsetXInput");
            Assert.Equal(0, anchor.Minimum);
            Assert.Equal(1, anchor.Maximum);
            Assert.Equal(0.01m, anchor.Increment);
            Assert.Equal(0.01m, pivot.Increment);
            Assert.Equal(1, offset.Increment);
            draft.OffsetY.RawText = "17";
            var text = Assert.Single(anchor.GetVisualDescendants().OfType<TextBox>());
            Assert.True(text.Focus());
            text.SelectAll();
            window.KeyTextInput("7e-");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("AnchorXInput", draft.Validate());
            Assert.True(editor.FocusInvalidField("AnchorXInput"));
            Assert.True(anchor.IsFocused || text.IsFocused);

            UiTestActions.Press(window, Key.Escape);
            Dispatcher.UIThread.RunJobs();

            Assert.Null(draft.Validate());
            Assert.Equal(0.5m, anchor.Value);
            Assert.Equal("17", draft.OffsetY.RawText);
            Assert.Equal(17, draft.CreatePosition()!.Offset.Y);
            Assert.False(editor.FocusInvalidField("MissingInput"));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ActualVectorRenderShowsNaturalGlyphBoundsDistinctAnchorPivotAndOffset()
    {
        using var environment = new UiTestEnvironment();
        var diagram = CreateDiagram();
        using var pixels = Render(diagram);

        Assert.False(diagram.IsHitTestVisible);
        Assert.False(diagram.Focusable);
        AssertGreen(pixels.GetPixel(110, 45));
        var anchor = pixels.GetPixel(110, 60);
        Assert.True(anchor.Blue > 150 && anchor.Red < 100);
        var pivot = pixels.GetPixel(129, 50);
        Assert.True(pivot.Red > 150 && pivot.Green > 50 && pivot.Blue < 60);
        var offset = pixels.GetPixel(119, 55);
        Assert.True(offset.Red > 60 && offset.Green > 60 && offset.Blue > 60);
        Assert.Equal(SKColors.Black, pixels.GetPixel(100, 45));
    }

    [AvaloniaFact]
    public void ActualGlyphOutlineChangesWithNaturalMeasurementsAndRotatedScale()
    {
        using var environment = new UiTestEnvironment();
        var diagram = CreateDiagram();
        using var original = Render(diagram);
        AssertGreen(original.GetPixel(110, 45));
        diagram.Geometry = diagram.Geometry! with { GlyphSize = new(20, 40) };
        using var resized = Render(diagram);
        Assert.Equal(SKColors.Black, resized.GetPixel(110, 45));
        AssertGreen(resized.GetPixel(119, 38));

        diagram.Geometry = CreateGeometry() with { Transform = new(ScaleX: 2, Rotation: 90) };
        using var rotated = Render(diagram);
        Assert.Equal(SKColors.Black, rotated.GetPixel(110, 45));
        AssertGreen(rotated.GetPixel(119, 25));
        AssertGreen(rotated.GetPixel(138, 75));
    }

    [AvaloniaFact]
    public void SharedEditorBindsTheReadOnlyDiagramAndWrapsWithoutChangingTheDraft()
    {
        using var environment = new UiTestEnvironment();
        var geometry = CreateGeometry();
        var style = new SubtitleStyle { FontFamily = "sans-serif", Position = CreatePosition() };
        var draft = new SubtitlePositionDraft();
        draft.Load(style, geometry: geometry);
        var editor = new LayerPositionEditor { DataContext = draft };
        var window = new Window { Width = 480, Height = 700, Content = editor };
        var changes = 0;
        draft.Changed += (_, _) => changes++;
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            using (var frame = window.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
            }
            var diagram = editor.FindControl<LayerPositionDiagram>("PositionDiagram")!;
            var picker = editor.FindControl<AnchorPresetPicker>("AnchorPresets")!;
            Assert.Same(geometry, diagram.Geometry);
            Assert.Equal(style.Position, diagram.Position);
            Assert.False(diagram.IsHitTestVisible);
            Assert.False(diagram.Focusable);
            Assert.Equal(picker.Bounds.Y, diagram.Bounds.Y, 3);
            window.Width = 240;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            using (var frame = window.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
            }
            Assert.True(diagram.Bounds.Top >= picker.Bounds.Bottom);
            Assert.Equal(0, changes);
            Assert.Equal(style.Position, draft.CreatePosition());
            Assert.Equal("sans-serif", style.FontFamily);

            var updated = geometry with { GlyphSize = new(20, 40), Transform = new(Rotation: 90) };
            draft.UpdateGeometry(updated);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(updated, diagram.Geometry);
            Assert.Equal(0, changes);
            Assert.Equal(style.Position, draft.CreatePosition());
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void UnifiedDiagramKeepsMeasuredGlyphAnchorPivotAndOffsetOnTheExistingProjection()
    {
        using var environment = new UiTestEnvironment();
        var diagram = CreateDiagram();
        diagram.ShowMargins = true;
        diagram.Margins = new(20, 60, 10);
        diagram.IsExplicit = true;
        using var pixels = Render(diagram);

        AssertGreen(pixels.GetPixel(110, 45));
        var anchor = pixels.GetPixel(110, 60);
        Assert.True(anchor.Blue > 150 && anchor.Red < 100);
        var pivot = pixels.GetPixel(129, 50);
        Assert.True(pivot.Red > 150 && pivot.Green > 50 && pivot.Blue < 60);
        Assert.True(pixels.GetPixel(20, 40).Red > 30);
        Assert.True(pixels.GetPixel(180, 40).Red > 30);
        Assert.Equal(SKColors.Black, pixels.GetPixel(100, 45));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidMarginDraftSuppressesMeasuredGeometryAndLeavesOnlyTheCanvas(bool missing)
    {
        using var environment = new UiTestEnvironment();
        var diagram = CreateDiagram();
        diagram.ShowMargins = true;
        diagram.Margins = missing ? null : new SubtitleMargins(double.NaN, 60, 10);
        using var pixels = Render(diagram);

        Assert.Equal(SKColors.Black, pixels.GetPixel(110, 45));
        Assert.Equal(SKColors.Black, pixels.GetPixel(110, 60));
        Assert.Equal(SKColors.Black, pixels.GetPixel(129, 50));
        Assert.Equal(SKColors.Black, pixels.GetPixel(20, 40));
        Assert.Equal(SKColors.Black, pixels.GetPixel(9, 129));
        Assert.True(pixels.GetPixel(14, 60).Red > 60);
    }

    [AvaloniaTheory]
    [InlineData(1, 100, 16)]
    [InlineData(7, 100, 104)]
    public void MissingMeasurementUsesCanvasAndActiveMarginsWithoutInventingGlyphs(int alignment, int x, int y)
    {
        using var environment = new UiTestEnvironment();
        var diagram = CreateDiagram();
        diagram.Geometry = null;
        diagram.CanvasWidth = 200;
        diagram.CanvasHeight = 100;
        diagram.ShowMargins = true;
        diagram.Alignment = alignment;
        diagram.Margins = new(20, 60, 10);
        using var pixels = Render(diagram);

        Assert.True(pixels.GetPixel(20, 40).Red > 30);
        Assert.True(pixels.GetPixel(180, 40).Red > 30);
        Assert.True(pixels.GetPixel(x, y).Red > 30);
        Assert.Equal(SKColors.Black, pixels.GetPixel(110, 60));
        Assert.Equal(SKColors.Black, pixels.GetPixel(9, 129));
        diagram.IsExplicit = true;
        using var explicitPixels = Render(diagram);
        Assert.Equal(SKColors.Black, explicitPixels.GetPixel(x, y));
        Assert.True(explicitPixels.GetPixel(20, 40).Red > 30);
        Assert.True(explicitPixels.GetPixel(180, 40).Red > 30);
    }

    [AvaloniaFact]
    public void MissingMeasurementWithMiddleAlignmentIgnoresVerticalMarginsAndKeepsTheCanvasAspect()
    {
        using var environment = new UiTestEnvironment();
        var diagram = CreateDiagram();
        diagram.Geometry = null;
        diagram.CanvasWidth = 200;
        diagram.CanvasHeight = 100;
        diagram.ShowMargins = true;
        diagram.Alignment = 4;
        diagram.Margins = new(20, 60, 10);
        using var first = Render(diagram);
        diagram.Margins = new(20, 60, 90);
        using var second = Render(diagram);
        Assert.Equal(first.Bytes, second.Bytes);
        diagram.CanvasWidth = 100;
        using var square = Render(diagram);
        Assert.NotEqual(second.GetPixel(14, 60), square.GetPixel(14, 60));
        Assert.True(square.GetPixel(62, 60).Red > 60);
    }

    [AvaloniaFact]
    public void MarginOverlayDoesNotChangeOffCanvasRotatedMeasurementProjection()
    {
        using var environment = new UiTestEnvironment();
        var diagram = CreateDiagram();
        diagram.Geometry = CreateGeometry() with { Transform = new(X: 40, Y: 15, ScaleX: 2, ScaleY: 0.75, Rotation: 43) };
        diagram.Position = CreatePosition() with { Offset = new(200, -90) };
        using var original = Render(diagram);
        diagram.ShowMargins = true;
        diagram.Margins = new(20, 60, 10);
        diagram.IsExplicit = true;
        using var unified = Render(diagram);

        foreach (var color in new[] { SKColors.RoyalBlue, SKColors.DarkOrange, SKColors.SeaGreen })
        {
            Assert.Equal(FindPixels(original, color), FindPixels(unified, color));
        }
    }

    private static List<(int X, int Y)> FindPixels(SKBitmap pixels, SKColor color)
    {
        var matches = new List<(int X, int Y)>();
        for (var y = 0; y < pixels.Height; y++)
        {
            for (var x = 0; x < pixels.Width; x++)
            {
                if (pixels.GetPixel(x, y) == color)
                {
                    matches.Add((x, y));
                }
            }
        }
        return matches;
    }

    private static LayerPositionDiagram CreateDiagram()
    {
        var diagram = new LayerPositionDiagram
        {
            Geometry = CreateGeometry(), Position = CreatePosition(), Foreground = Brushes.White
        };
        diagram.Measure(new(220, 140));
        diagram.Arrange(new(0, 0, 220, 140));
        return diagram;
    }

    private static LayerPositionDraftGeometry CreateGeometry()
    {
        return new(new(200, 100), new(20, 30), new(40, 20), new());
    }

    private static SubtitlePosition CreatePosition()
    {
        return new() { Anchor = new(0.5, 0.5), Pivot = new(0.5, 0.5), Offset = new(20, -10) };
    }

    private static SKBitmap Render(LayerPositionDiagram diagram)
    {
        using var target = new RenderTargetBitmap(new(220, 140), new(96, 96));
        using (var drawing = target.CreateDrawingContext())
        {
            drawing.DrawRectangle(Brushes.Black, null, new Rect(0, 0, 220, 140));
            diagram.Render(drawing);
        }
        using var stream = new MemoryStream();
        target.Save(stream, PngBitmapEncoderOptions.Default);
        stream.Position = 0;
        return SKBitmap.Decode(stream);
    }

    private static void AssertGreen(SKColor color)
    {
        Assert.True(color.Green > 80 && color.Red < 90 && color.Blue < 130, $"Expected natural glyph outline, received {color}.");
    }
}
