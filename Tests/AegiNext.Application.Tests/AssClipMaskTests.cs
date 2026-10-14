using System.Collections.Immutable;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.SubtitleFormats;
using AegiNext.Testing.SubtitleFormats;

namespace AegiNext.Application.Tests;

public sealed class AssClipMaskTests
{
    [Theory]
    [InlineData("clip", false)]
    [InlineData("iclip", true)]
    public void RectangleImportIsCarriedIntoItsClipAtomically(string tag, bool inverted)
    {
        var parsed = AssSubtitleFormat.Parse(File($"{{\\{tag}(10,20,30,40)}}字幕"), 640, 360);
        var imported = ProjectEditingOperations.ImportSubtitleLines(new() { Width = 640, Height = 360 }, parsed, "ASS");
        var mask = Assert.IsType<RectangleClipMask>(Assert.Single(imported.Layers).Mask);
        Assert.Equal(new ScenePoint(10, 20), mask.TopLeft);
        Assert.Equal(new ScenePoint(30, 40), mask.BottomRight);
        Assert.Equal(inverted, mask.Inverted);
        Assert.DoesNotContain(parsed.Diagnostics, diagnostic => diagnostic.Code == "Ass.UnsupportedTag");
        var exported = AssSubtitleFormat.Write(imported);
        Assert.Contains($"\\{tag}(10,20,30,40)", exported.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(exported.Diagnostics, diagnostic => diagnostic.Code == "Subtitle.Composition");
    }

    [Fact]
    public void DrawingScaleContoursAndRelativeBezierHandlesRoundTrip()
    {
        var parsed = AssSubtitleFormat.Parse(File("{\\iclip(2,m 0 0 l 200 0 200 200 0 200 m 40 40 b 40 120 120 120 120 40 l 40 40)}a"), 640, 360);
        var mask = Assert.IsType<VectorClipMask>(Assert.Single(parsed.Clips).Mask);
        Assert.Equal(2, mask.Contours.Length);
        Assert.Equal(new ScenePoint(100, 100), mask.Contours[0].Nodes[2].Position);
        Assert.Equal(new ScenePoint(0, 40), mask.Contours[1].Nodes[0].OutHandle);
        Assert.Equal(new ScenePoint(0, 40), mask.Contours[1].Nodes[1].InHandle);
        var document = ProjectEditingOperations.ImportSubtitleLines(new() { Width = 640, Height = 360 }, parsed, "ASS");
        var written = AssSubtitleFormat.Write(document);
        var reimported = Assert.IsType<VectorClipMask>(Assert.Single(AssSubtitleFormat.Parse(written.Text, 640, 360).Clips).Mask);
        Assert.Equal(mask.Contours.SelectMany(contour => contour.Nodes).Select(node => (node.Position, node.InHandle, node.OutHandle)),
            reimported.Contours.SelectMany(contour => contour.Nodes).Select(node => (node.Position, node.InHandle, node.OutHandle)));
    }

    [Theory]
    [InlineData("\\clip(0,0,100,100)\\t(\\clip(20,40,120,140))", 1)]
    [InlineData("\\clip(0,0,100,100)\\t(2,\\clip(20,40,120,140))", 2)]
    [InlineData("\\clip(0,0,100,100)\\t(0,2000,\\clip(20,40,120,140))", 1)]
    [InlineData("\\clip(0,0,100,100)\\t(0,2000,0.5,\\clip(20,40,120,140))", 0.5)]
    public void AllTransformFormsUseTheSharedPowerEvaluator(string tags, double exponent)
    {
        var result = AssSubtitleFormat.Parse(File("{" + tags + "}a"), 640, 360);
        var document = ProjectEditingOperations.ImportSubtitleLines(new() { Width = 640, Height = 360 }, result, "ASS");
        var layer = Assert.Single(document.Layers);
        var mask = Assert.IsType<RectangleClipMask>(SceneEvaluator.EvaluateMask(layer, new(1)));
        Assert.Equal(20 * Math.Pow(0.5, exponent), mask.TopLeft.X, 8);
        Assert.Equal(40 * Math.Pow(0.5, exponent), mask.TopLeft.Y, 8);
        Assert.All(layer.Tracks, track => Assert.Empty(track.Transforms));
        var exported = AssSubtitleFormat.Write(document);
        Assert.Contains("\\t(", exported.Text, StringComparison.Ordinal);
        var reread = ProjectEditingOperations.ImportSubtitleLines(new() { Width = 640, Height = 360 }, AssSubtitleFormat.Parse(exported.Text, 640, 360), "ASS");
        Assert.Equal(mask.TopLeft, Assert.IsType<RectangleClipMask>(SceneEvaluator.EvaluateMask(Assert.Single(reread.Layers), new(1))).TopLeft);
    }

    [Fact]
    public void TransformWithoutBaseClipStartsAtTheTargetCanvasAndZeroEndUsesEventDuration()
    {
        var parsed = AssSubtitleFormat.Parse(File("{\\t(0,0,\\clip(20,40,120,140))}a"), 640, 360);
        var document = ProjectEditingOperations.ImportSubtitleLines(new() { Width = 640, Height = 360 }, parsed, "ASS");
        var layer = Assert.Single(document.Layers);
        var initial = Assert.IsType<RectangleClipMask>(SceneEvaluator.EvaluateMask(layer, MediaTime.Zero));
        Assert.Equal(new ScenePoint(0, 0), initial.TopLeft);
        Assert.Equal(new ScenePoint(640, 360), initial.BottomRight);
        var middle = Assert.IsType<RectangleClipMask>(SceneEvaluator.EvaluateMask(layer, new(1)));
        Assert.Equal(new ScenePoint(10, 20), middle.TopLeft);
        Assert.Equal(new ScenePoint(380, 250), middle.BottomRight);
    }

    [Fact]
    public void OverlappingTransformsPreserveSourceOrderAndAcceleration()
    {
        var parsed = AssSubtitleFormat.Parse(File("{\\clip(0,0,100,100)\\t(0,2000,2,\\clip(100,0,200,100))\\t(500,1500,0.5,\\clip(200,0,300,100))}a"), 640, 360);
        var document = ProjectEditingOperations.ImportSubtitleLines(new() { Width = 640, Height = 360 }, parsed, "ASS");
        var layer = Assert.Single(document.Layers);
        Assert.All(layer.Tracks, track => Assert.Equal(2, track.Transforms.Length));
        var actual = Assert.IsType<RectangleClipMask>(SceneEvaluator.EvaluateMask(layer, new(1)));
        Assert.Equal(25 + (200 - 25) * Math.Sqrt(0.5), actual.TopLeft.X, 8);
        var exported = AssSubtitleFormat.Write(document);
        var reread = ProjectEditingOperations.ImportSubtitleLines(new() { Width = 640, Height = 360 }, AssSubtitleFormat.Parse(exported.Text, 640, 360), "ASS");
        Assert.Equal(actual.TopLeft.X, Assert.IsType<RectangleClipMask>(SceneEvaluator.EvaluateMask(Assert.Single(reread.Layers), new(1))).TopLeft.X, 8);
    }

    [Fact]
    public void AdvancedTextEditsPreserveNativeGeometryIdsAndTracksWhenMaskTagsAreUnchanged()
    {
        var parsed = AssSubtitleFormat.Parse(File("{\\clip(m 0 0 l 100 0 100 100 0 100)}a"), 640, 360);
        var document = ProjectEditingOperations.ImportSubtitleLines(new() { Width = 640, Height = 360 }, parsed, "ASS");
        var layer = Assert.Single(document.Layers);
        var node = Assert.IsType<VectorClipMask>(layer.Mask).Contours[0].Nodes[0];
        layer = layer with { Tracks = [new(new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, node.Id),
            [new(MediaTime.Zero, AnimationValue.FromVector(node.Position)), new(new(2), AnimationValue.FromVector(new(10, 20)))])] };
        var line = Assert.Single(document.Subtitles);
        var projection = AssTextProjection.Create(line, canvasWidth: 640, canvasHeight: 360, layer: layer);
        var edited = AssTextProjection.Apply(line, projection.Source + "b", canvasWidth: 640, canvasHeight: 360, layer: layer);
        Assert.Same(layer.Mask, edited.Mask);
        Assert.True(layer.Tracks.SequenceEqual(edited.MaskTracks));
        Assert.Same(layer.Tracks[0], edited.MaskTracks[0]);
        Assert.Equal("ab", edited.Line.Text);
        Assert.Throws<InvalidOperationException>(() => AssTextProjection.Apply(line,
            projection.Source.Replace("m 0 0", "m 0 0 l 64 64", StringComparison.Ordinal), canvasWidth: 640, canvasHeight: 360, layer: layer));
    }

    [Fact]
    public void ChangingOnlyNonMaskTagsInsideATransformPreservesNativeMaskOperationIds()
    {
        var imported = ProjectEditingOperations.ImportSubtitleLines(new() { Width = 640, Height = 360 },
            AssSubtitleFormat.Parse(File("{\\clip(0,0,100,100)\\t(0,2000,\\clip(20,20,120,120))\\t(500,1500,\\clip(40,40,140,140))}a"), 640, 360), "ASS");
        var line = imported.Subtitles[0];
        var layer = imported.Layers[0];
        var source = AssTextProjection.Create(line, canvasWidth: 640, canvasHeight: 360, layer: layer).Source;
        var changedSource = source.Replace("\\clip(20,20,120,120)", "\\clip(20,20,120,120)\\fs40", StringComparison.Ordinal);
        Assert.NotEqual(source, changedSource);
        var edited = AssTextProjection.Apply(line, changedSource, canvasWidth: 640, canvasHeight: 360, layer: layer);
        Assert.Same(layer.Mask, edited.Mask);
        Assert.Same(layer.Tracks[0], edited.MaskTracks[0]);
        Assert.Equal(layer.Tracks[0].Transforms[0].Id, edited.MaskTracks[0].Transforms[0].Id);
    }

    [Fact]
    public void LiteralEscapedBraceTextDoesNotChangeTheMaskTagIdentity()
    {
        var line = new SubtitleLine { Text = "{\\clip(1,2,3,4)}" };
        var mask = new RectangleClipMask { BottomRight = new(100, 100) };
        var layer = new ProjectLayer { Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Mask = mask };
        var source = AssTextProjection.Create(line, layer: layer).Source;
        var result = AssTextProjection.Apply(line, source.Replace("clip(1,2,3,4)", "clip(2,3,4,5)", StringComparison.Ordinal), layer: layer);
        Assert.Same(mask, result.Mask);
        Assert.Equal("{\\clip(2,3,4,5)}", result.Line.Text);
    }

    [Fact]
    public void AdvancedGeometryEditsKeepNativePlacementAndDoNotApplyTheMaskTransformTwice()
    {
        var line = new SubtitleLine { Text = "a" };
        var mask = new VectorClipMask
        {
            Placement = new() { Anchor = new(0.25, 0.75), Pivot = new(0.4, 0.5), Offset = new(-440, -760) },
            ReferenceFrame = new() { Size = new(100, 100) },
            Transform = new() { Position = new(10, 20), Scale = new(2, 2) },
            Contours = [new() { Nodes = [new() { Position = new(0, 0) }, new() { Position = new(100, 0) }, new() { Position = new(100, 100) }] }] };
        var layer = new ProjectLayer { Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Mask = mask,
            Tracks = [new(AnimationProperty.MASK_POSITION, [new(MediaTime.Zero, AnimationValue.FromVector(new(10, 20))), new(new(2), AnimationValue.FromVector(new(20, 30)))])] };
        var source = AssTextProjection.Create(line, layer: layer).Source;
        var edited = AssTextProjection.Apply(line, source.Replace("m -1920 -1920", "m -1280 -1920", StringComparison.Ordinal), layer: layer);
        var changed = Assert.IsType<VectorClipMask>(edited.Mask);
        Assert.Equal(mask.Transform, changed.Transform);
        Assert.Same(mask.Placement, changed.Placement);
        Assert.Same(mask.ReferenceFrame, changed.ReferenceFrame);
        Assert.Equal(mask.Contours[0].Nodes[0].Id, changed.Contours[0].Nodes[0].Id);
        Assert.Equal(new ScenePoint(5, 0), changed.Contours[0].Nodes[0].Position);
        Assert.Same(layer.Tracks[0], Assert.Single(edited.MaskTracks));
    }

    [Fact]
    public void AnimatedVectorExportExpandsAtProjectFramesAndReloadsStaticClips()
    {
        var line = new SubtitleLine { Text = "a", End = new(1) };
        var mask = new VectorClipMask { Contours = [new() { Nodes = [new() { Position = new(0, 0) }, new() { Position = new(100, 0) }, new() { Position = new(100, 100) }] }] };
        var layer = new ProjectLayer { Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, End = line.End, Mask = mask,
            Tracks = [new(AnimationProperty.MASK_POSITION, [new(MediaTime.Zero, AnimationValue.FromVector(new(0, 0))), new(new(1), AnimationValue.FromVector(new(20, 0)))])] };
        var document = new ProjectDocument { FrameRate = new(4, 1), Subtitles = [line], Layers = [layer] };
        var exported = AssSubtitleFormat.Write(document);
        Assert.Contains(exported.Diagnostics, diagnostic => diagnostic.Code == "Ass.MaskAnimationExpanded");
        var parsed = AssSubtitleFormat.Parse(exported.Text);
        Assert.Equal(4, parsed.Clips.Length);
        Assert.All(parsed.Clips, clip => Assert.Empty(clip.Tracks));
        Assert.Equal(line.Text, parsed.Clips[0].Line.Text);
        Assert.Equal(new ScenePoint(5, 0), Assert.IsType<VectorClipMask>(parsed.Clips[1].Mask).Contours[0].Nodes[0].Position);
        Assert.Contains(SubtitleFormatLossAnalysis.ForSrt(document), diagnostic => diagnostic.Code == "Srt.Mask");
        Assert.Contains(SubtitleFormatLossAnalysis.ForSrt(document), diagnostic => diagnostic.Code == "Srt.Animation");
    }

    [Theory]
    [InlineData(0.0000001)]
    [InlineData(1000000000)]
    public void ArbitraryPositiveAccelerationAndInstantOrderedOperationRemainValid(double acceleration)
    {
        var body = "{\\clip(0,0,100,100)\\t(0,2000," + acceleration.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
            ",\\clip(20,0,120,100))\\t(500,500,\\clip(40,0,140,100))}a";
        var imported = ProjectEditingOperations.ImportSubtitleLines(new() { Width = 640, Height = 360 }, AssSubtitleFormat.Parse(File(body), 640, 360), "ASS");
        var layer = Assert.Single(imported.Layers);
        Assert.Equal(40, Assert.IsType<RectangleClipMask>(SceneEvaluator.EvaluateMask(layer, new(1))).TopLeft.X);
        Assert.Equal(acceleration, layer.Tracks[0].Transforms[0].Acceleration);
        var exported = AssSubtitleFormat.Write(imported);
        Assert.Contains("500,500,1,", exported.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ZeroAccelerationRemainsAnOrderedOperationWithExactSourceTimes()
    {
        var result = AssSubtitleFormat.Parse(File("{\\clip(0,0,100,100)\\t(500,1500,0,\\clip(20,40,120,140))}a"), 640, 360);
        var document = ProjectEditingOperations.ImportSubtitleLines(new() { Width = 640, Height = 360 }, result, "ASS");
        var layer = Assert.Single(document.Layers);
        var operation = Assert.Single(layer.Tracks[0].Transforms);
        Assert.Equal(new MediaTime(1, 2), operation.Start);
        Assert.Equal(new MediaTime(3, 2), operation.End);
        Assert.Equal(0, operation.Acceleration);
        Assert.Equal(0, Assert.IsType<RectangleClipMask>(SceneEvaluator.EvaluateMask(layer, new(499, 1000))).TopLeft.X);
        Assert.Equal(20, Assert.IsType<RectangleClipMask>(SceneEvaluator.EvaluateMask(layer, new(1, 2))).TopLeft.X);
        Assert.Contains("500,1500,0,", AssSubtitleFormat.Write(document).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void NegativeAccelerationAndMixedModesReportFiniteRepresentationLoss()
    {
        var result = AssSubtitleFormat.Parse(File("{\\clip(0,0,100,100)\\t(0,2000,-1,\\clip(20,40,120,140))\\t(0,2000,\\iclip(10,20,110,120))}a"), 640, 360);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Ass.MaskAcceleration");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Ass.MixedMaskModes");
        Assert.Empty(Assert.Single(result.Clips).Tracks);
        Assert.Equal(new ScenePoint(0, 0), Assert.IsType<RectangleClipMask>(result.Clips[0].Mask).TopLeft);
    }

    [Fact]
    public void SplineAndExtensionCommandsNormalizeToCubicDrawing()
    {
        var parsed = AssSubtitleFormat.Parse(File("{\\clip(m 0 0 s 60 0 60 60 0 60 p -60 60 c)}a"), 640, 360);
        var vector = Assert.IsType<VectorClipMask>(Assert.Single(parsed.Clips).Mask);
        Assert.True(vector.Contours[0].Nodes.Length >= 4);
        Assert.Contains(vector.Contours[0].Nodes, node => node.OutHandle != default);
        var imported = ProjectEditingOperations.ImportSubtitleLines(new() { Width = 640, Height = 360 }, parsed, "ASS");
        var text = AssSubtitleFormat.Write(imported).Text;
        Assert.Contains(" b ", text, StringComparison.Ordinal);
        Assert.DoesNotContain(" s ", text, StringComparison.Ordinal);
        Assert.DoesNotContain(" p -", text, StringComparison.Ordinal);
        Assert.Single(AssSubtitleFormat.Parse(text, 640, 360).Clips);
    }

    [Theory]
    [InlineData(KeyframeInterpolation.EASE_IN)]
    [InlineData(KeyframeInterpolation.HOLD)]
    public void NativeRectangleExportIncludesExactlyRepresentableEaseInAndHoldCurves(KeyframeInterpolation interpolation)
    {
        var line = new SubtitleLine { Text = "a", End = new(2) };
        var layer = new ProjectLayer { Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, End = line.End,
            Mask = new RectangleClipMask { BottomRight = new(100, 100) }, Tracks =
            [new(AnimationProperty.MASK_RECTANGLE_TOP_LEFT, [new(MediaTime.Zero, AnimationValue.FromVector(new(0, 0)), interpolation),
                new(new(1), AnimationValue.FromVector(new(20, 20)))])] };
        var written = AssSubtitleFormat.Write(new() { Subtitles = [line], Layers = [layer] });
        Assert.DoesNotContain(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.MaskAnimationExpanded");
        var imported = ProjectEditingOperations.ImportSubtitleLines(new(), AssSubtitleFormat.Parse(written.Text), "ASS");
        Assert.Equal(Assert.IsType<RectangleClipMask>(SceneEvaluator.EvaluateMask(layer, new(1, 2))).TopLeft,
            Assert.IsType<RectangleClipMask>(SceneEvaluator.EvaluateMask(imported.Layers[0], new(1, 2))).TopLeft);
        Assert.Equal(Assert.IsType<RectangleClipMask>(SceneEvaluator.EvaluateMask(layer, new(1))).TopLeft,
            Assert.IsType<RectangleClipMask>(SceneEvaluator.EvaluateMask(imported.Layers[0], new(1))).TopLeft);
        Assert.Contains(interpolation == KeyframeInterpolation.HOLD ? "1000,1000,1," : "0,1000,2,", written.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void HighFrameRateQuantizationMergesSamplesIntoNonOverlappingIntervals()
    {
        var line = new SubtitleLine { Text = "a", End = new(1, 10) };
        var layer = new ProjectLayer { Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, End = line.End,
            Mask = new RectangleClipMask { BottomRight = new(100, 100) }, Tracks =
            [new(AnimationProperty.MASK_ROTATION, [new(MediaTime.Zero, 0), new(line.End, 90)])] };
        var written = AssSubtitleFormat.Write(new() { FrameRate = new(240, 1), Subtitles = [line], Layers = [layer] });
        var imported = AssSubtitleFormat.Parse(written.Text);
        Assert.Equal(10, imported.Clips.Length);
        Assert.Contains(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.MaskTimeQuantization");
        for (var index = 1; index < imported.Lines.Length; index++)
        {
            Assert.Equal(imported.Lines[index - 1].End, imported.Lines[index].Start);
        }
        Assert.Equal(MediaTime.Zero, imported.Lines[0].Start);
        Assert.Equal(line.End, imported.Lines[^1].End);
    }

    [Fact]
    public void ExpandedKaraokeSweepKeepsItsOriginalProgressAndRichText()
    {
        var line = new SubtitleLine { Text = "ab", End = new(1), InlineSpans = [new(1, 1, new() { Bold = true })],
            Karaoke = [new(0, 2, MediaTime.Zero, new(1), SceneColor.White) { HighlightKind = KaraokeHighlightKind.SWEEP }] };
        var layer = new ProjectLayer { Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, End = line.End,
            Mask = new RectangleClipMask { BottomRight = new(100, 100) },
            Tracks = [new(AnimationProperty.MASK_ROTATION, [new(MediaTime.Zero, 0), new(new(1), 45)])] };
        var document = new ProjectDocument { FrameRate = new(4, 1), Subtitles = [line], Layers = [layer] };
        var written = AssSubtitleFormat.Write(document);
        Assert.Contains("\\kt-50", written.Text, StringComparison.Ordinal);
        var reread = AssSubtitleFormat.Parse(written.Text);
        var clip = reread.Clips[2];
        Assert.Equal(new MediaTime(1, 2), clip.ContentOffset);
        Assert.Equal(line.Karaoke.Select(segment => (segment.Utf16Start, segment.Utf16Length, segment.Start, segment.End, segment.HighlightKind)),
            clip.Line.Karaoke.Select(segment => (segment.Utf16Start, segment.Utf16Length, segment.Start, segment.End, segment.HighlightKind)));
        var group = Assert.Single(clip.Line.Karaoke);
        Assert.Equal(MediaTime.Zero, group.Start);
        Assert.Equal(new MediaTime(1), group.End);
        Assert.Contains(clip.Line.InlineSpans, span => span.Utf16Start == 1 && span.Style.Bold == true);
    }

    [Fact]
    public void AdvancedTextAndMaskCommitUseOneUndoAndMissingTargetsFailAtomically()
    {
        var line = new SubtitleLine { Text = "a" };
        var editor = new ProjectEditor(SubtitleFormatTestDocument.Create(line));
        var original = editor.Snapshot;
        var edited = AssTextProjection.Apply(line, "{\\clip(0,0,100,100)}b");
        editor.ApplyAssTextEdit(line.Id, edited);
        Assert.Equal("b", editor.Snapshot.Subtitles[0].Text);
        Assert.IsType<RectangleClipMask>(editor.Snapshot.Layers[0].Mask);
        Assert.True(editor.Undo());
        Assert.Same(original, editor.Snapshot);
        Assert.False(editor.Undo());
        var invalid = edited with { MaskTracks = [new(new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, Guid.NewGuid()), [new(MediaTime.Zero, AnimationValue.FromVector(new(0, 0)))])] };
        Assert.Throws<InvalidDataException>(() => editor.ApplyAssTextEdit(line.Id, invalid));
        Assert.Same(original, editor.Snapshot);
        Assert.False(editor.Undo());
    }

    [Fact]
    public void ExpandedGeometryQuantizationReportsOneDiagnosticPerClip()
    {
        var line = new SubtitleLine { Text = "a", End = new(1) };
        var vector = new VectorClipMask { Contours = [new() { Nodes = [new() { Position = new(0.001, 0.002) },
            new() { Position = new(10.003, 0.004) }, new() { Position = new(10.005, 10.006) }] }] };
        var layer = new ProjectLayer { Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, End = line.End, Mask = vector,
            Tracks = [new(AnimationProperty.MASK_ROTATION, [new(MediaTime.Zero, 0), new(line.End, 45)])] };
        var result = AssSubtitleFormat.Write(new() { FrameRate = new(12, 1), Subtitles = [line], Layers = [layer] });
        Assert.Single(result.Diagnostics.Where(diagnostic => diagnostic.Code == "Ass.MaskQuantization" && diagnostic.SubtitleId == line.Id));
    }

    private static string File(string body) => """
        [Script Info]
        ScriptType: v4.00+
        PlayResX: 640
        PlayResY: 360
        [V4+ Styles]
        Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, Outline, Shadow, Alignment, MarginV
        Style: Default,Noto Sans,20,&H00FFFFFF,&H000000FF,&H00000000,&H80000000,0,0,0,0,2,2,2,20
        [Events]
        Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
        Dialogue: 0,0:00:00.00,0:00:02.00,Default,,0,0,0,,
        """ + body;
}
