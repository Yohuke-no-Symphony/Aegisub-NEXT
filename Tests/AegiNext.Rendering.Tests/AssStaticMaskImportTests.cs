using AegiNext.Core.Projects;
using AegiNext.Rendering.SubtitleFormats;
using AegiNext.SubtitleFormats;
using SkiaSharp;

namespace AegiNext.Rendering.Tests;

public sealed class AssStaticMaskImportTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void IndependentVectorAndRectangleSlotsPreserveAllInversionCombinations(bool vectorInverted, bool rectangleInverted)
    {
        var vectorTag = vectorInverted ? "iclip" : "clip";
        var rectangleTag = rectangleInverted ? "iclip" : "clip";
        var result = Parse($"{{\\{vectorTag}(m 20 20 l 100 20 100 100 20 100)\\{rectangleTag}(60,60,140,140)}}x");
        var mask = Assert.Single(result.Clips).Mask!;
        for (var y = 5; y < 160; y += 10)
        {
            for (var x = 5; x < 160; x += 10)
            {
                var insideVector = x > 20 && x < 100 && y > 20 && y < 100;
                var insideRectangle = x > 60 && x < 140 && y > 60 && y < 140;
                Assert.Equal((insideVector != vectorInverted) && (insideRectangle != rectangleInverted), Contains(mask, x, y));
            }
        }
        Assert.Contains(result.Diagnostics, item => item.Code == "Ass.MaskGeometryPrecision");
        Assert.DoesNotContain(result.Diagnostics, item => item.Code == "Ass.MixedMaskModes");
    }

    [Fact]
    public void EmptyIntersectionRemainsAnEmptyMaskRatherThanUnclippedText()
    {
        var result = Parse(@"{\clip(m 0 0 l 10 0 10 10 0 10)\clip(20,20,30,30)}x");
        var mask = Assert.IsType<RectangleClipMask>(Assert.Single(result.Clips).Mask);
        Assert.Equal(mask.TopLeft, mask.BottomRight);
        Assert.False(mask.Inverted);
        Assert.False(Contains(mask, 5, 5));
    }

    [Fact]
    public void LaterVectorIsIgnoredEvenIfItsDrawingIsInvalid()
    {
        var result = Parse(@"{\clip(m 0 0 l 100 0 100 100 0 100)\iclip(m invalid)\clip(20,20,80,80)}x");
        var mask = Assert.Single(result.Clips).Mask!;
        Assert.True(Contains(mask, 50, 50));
        Assert.False(Contains(mask, 10, 10));
        Assert.Single(result.Diagnostics, item => item.Code == "Ass.DuplicateMask");
    }

    [Fact]
    public void EmptyFirstVectorStillOccupiesTheSourceSlot()
    {
        var result = Parse(@"{\clip(m)\clip(m 0 0 l 100 0 100 100)}x");
        Assert.Null(Assert.Single(result.Clips).Mask);
        Assert.Contains(result.Diagnostics, item => item.Code == "Ass.EmptyVectorMask");
        Assert.Contains(result.Diagnostics, item => item.Code == "Ass.DuplicateMask");
    }

    [Fact]
    public void LaterRectangleCancelsRectangleAnimationAndKeepsTheFirstVector()
    {
        var result = Parse(@"{\clip(0,0,100,100)\t(0,2000,\clip(100,100,200,200))\clip(m 10 10 l 90 10 90 90 10 90)\clip(20,20,80,80)}x");
        var clip = Assert.Single(result.Clips);
        Assert.Empty(clip.Tracks);
        Assert.True(Contains(clip.Mask!, 50, 50));
        Assert.False(Contains(clip.Mask!, 15, 15));
        Assert.DoesNotContain(result.Diagnostics, item => item.Code == "Ass.MixedMaskAnimation");
    }

    [Fact]
    public void AnimatedMixedClippingDoesNotAttachRectangleTracksToAVectorMask()
    {
        var result = Parse(@"{\clip(m 10 10 l 90 10 90 90 10 90)\t(0,2000,\clip(20,20,80,80))}x");
        Assert.IsType<VectorClipMask>(Assert.Single(result.Clips).Mask);
        Assert.Empty(result.Clips[0].Tracks);
        Assert.Contains(result.Diagnostics, item => item.Code == "Ass.MixedMaskAnimation");
    }

    [Fact]
    public void NonzeroWindingHolesSurviveRectangleIntersectionAndAssRoundTrip()
    {
        var result = Parse(@"{\clip(m 0 0 l 100 0 100 100 0 100 m 25 25 l 25 75 75 75 75 25)\clip(10,10,90,90)}x");
        var mask = Assert.IsType<VectorClipMask>(Assert.Single(result.Clips).Mask);
        Assert.True(Contains(mask, 15, 15));
        Assert.False(Contains(mask, 50, 50));
        var line = result.Lines[0];
        var document = new ProjectDocument
        {
            Width = 640, Height = 360, Subtitles = result.Lines,
            Layers = [new() { Id = line.Id, Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End, Mask = mask }]
        };
        var roundTrip = Parse(AssSubtitleFormat.Write(document).Text, completeFile: true);
        Assert.True(Contains(roundTrip.Clips[0].Mask!, 15, 15));
        Assert.False(Contains(roundTrip.Clips[0].Mask!, 50, 50));
    }

    private static AssImportResult Parse(string body, bool completeFile = false)
    {
        return AssSubtitleFormat.Parse(completeFile ? body : """
            [Script Info]
            ScriptType: v4.00+
            PlayResX: 640
            PlayResY: 360
            WrapStyle: 2
            [V4+ Styles]
            Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Outline, Shadow
            Style: Default,Noto Sans,20,&H00FFFFFF,&H000000FF,&H00000000,&H00000000,0,0
            [Events]
            Format: Layer, Start, End, Style, Text
            Dialogue: 0,0:00:00.00,0:00:02.00,Default,
            """ + body, 640, 360, maskGeometryCombiner: new AssMaskGeometryCombiner());
    }

    private static bool Contains(ClipMask mask, float x, float y)
    {
        using var path = new SKPath { FillType = SKPathFillType.Winding };
        if (mask is RectangleClipMask rectangle)
        {
            path.AddRect(new((float)rectangle.TopLeft.X, (float)rectangle.TopLeft.Y,
                (float)rectangle.BottomRight.X, (float)rectangle.BottomRight.Y));
        }
        else
        {
            foreach (var contour in ((VectorClipMask)mask).Contours)
            {
                path.MoveTo((float)contour.Nodes[0].Position.X, (float)contour.Nodes[0].Position.Y);
                for (var index = 0; index < contour.Nodes.Length; index++)
                {
                    var first = contour.Nodes[index];
                    var second = contour.Nodes[(index + 1) % contour.Nodes.Length];
                    path.CubicTo((float)(first.Position.X + first.OutHandle.X), (float)(first.Position.Y + first.OutHandle.Y),
                        (float)(second.Position.X + second.InHandle.X), (float)(second.Position.Y + second.InHandle.Y),
                        (float)second.Position.X, (float)second.Position.Y);
                }
                path.Close();
            }
        }
        return path.Contains(x, y) != mask.Inverted;
    }
}
