using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.SubtitleFormats;
using AegiNext.Testing.SubtitleFormats;

namespace AegiNext.SubtitleFormats.Tests;

public sealed class AssScaleResetImportTests
{
    [Theory]
    [InlineData(@"\fscx200\fscy300\fsc")]
    [InlineData(@"\fscx200\fscy300\fsc123")]
    [InlineData(@"\fscx200\fscy300\t(1000,1500,\fsc)")]
    [InlineData(@"\t(0,2000,\fscx300\fscy200)\t(1000,1500,\fsc)")]
    [InlineData(@"\fscx200\fscy300\t(1000,1500,\t(100,200,\fsc))")]
    public void ResetRestoresBothStyleAxesImmediatelyAndRemovesEarlierScaleOperations(string tags)
    {
        var source = AssBoundarySource.File("{" + tags + @"\frz30}x")
            .Replace(",100,100,0,0,1,", ",125,80,0,0,1,", StringComparison.Ordinal);
        var imported = AssSubtitleFormat.Parse(source, 640, 360);
        var clip = Assert.Single(imported.Clips);

        Assert.Equal(new ScenePoint(1.25, 0.8), clip.Transform.Scale);
        Assert.Equal(-30, clip.Transform.Rotation);
        Assert.DoesNotContain(clip.Tracks, track => track.Property == AnimationProperty.SCALE);
        Assert.DoesNotContain(imported.Diagnostics, diagnostic => diagnostic.Code == "Ass.UnsupportedTag");
    }

    [Fact]
    public void ResetUsesTheNamedResetStyleRatherThanTheOriginalStyle()
    {
        var source = AssBoundarySource.File(@"{\rAlternate\fscx200\fscy300\fsc}x")
            .Replace("Style: Alternate,Noto Sans,30,&H800000FF,&H4000FF00,&H20FF0000,&H60FFFFFF,0,0,0,0,100,100,",
                "Style: Alternate,Noto Sans,30,&H800000FF,&H4000FF00,&H20FF0000,&H60FFFFFF,0,0,0,0,150,75,", StringComparison.Ordinal);

        Assert.Equal(new ScenePoint(1.5, 0.75), Assert.Single(AssSubtitleFormat.Parse(source, 640, 360).Clips).Transform.Scale);
    }

    [Fact]
    public void ScaleAnimationAfterAnImmediateResetStartsFromTheResetAxes()
    {
        var imported = AssSubtitleFormat.Parse(AssBoundarySource.File(@"{\fscx300\fscy250\t(0,2000,\fsc\fscx200)}x"), 640, 360);
        var track = Assert.Single(Assert.Single(imported.Clips).Tracks, track => track.Property == AnimationProperty.SCALE);

        Assert.Equal(new ScenePoint(1, 1), SceneEvaluator.EvaluateVectorTrack(track, MediaTime.Zero));
        Assert.Equal(new ScenePoint(1.5, 1), SceneEvaluator.EvaluateVectorTrack(track, new(1)));
        Assert.Equal(new ScenePoint(2, 1), SceneEvaluator.EvaluateVectorTrack(track, new(2)));
    }

    [Fact]
    public void ImmediateResetAfterAnOperationInTheSameTransformCancelsIt()
    {
        var imported = AssSubtitleFormat.Parse(AssBoundarySource.File(@"{\t(0,2000,\fscx200\fsc)}x"), 640, 360);

        Assert.Equal(new ScenePoint(1, 1), Assert.Single(imported.Clips).Transform.Scale);
        Assert.DoesNotContain(Assert.Single(imported.Clips).Tracks, track => track.Property == AnimationProperty.SCALE);
    }

    [Theory]
    [InlineData(@"\t(1500,1000,\fsc)")]
    [InlineData(@"\t(0,2000,-1,\fsc)")]
    public void UnsupportedTransformTimingDoesNotSwallowAnImmediateScaleReset(string transform)
    {
        var imported = AssSubtitleFormat.Parse(AssBoundarySource.File(@"{\fscx200\fscy300" + transform + "}x"), 640, 360);

        Assert.Equal(new ScenePoint(1, 1), Assert.Single(imported.Clips).Transform.Scale);
        Assert.Contains(imported.Diagnostics, diagnostic => diagnostic.Code == "Ass.TransformTiming");
    }

    [Fact]
    public void ResetDoesNotCancelRotationOrFontSizeAnimation()
    {
        var imported = AssSubtitleFormat.Parse(AssBoundarySource.File(@"{\t(0,2000,\frz40\fs30\fscx200)\fsc}x"), 640, 360);
        var tracks = Assert.Single(imported.Clips).Tracks;

        Assert.Contains(tracks, track => track.Property == AnimationProperty.ROTATION_3D);
        Assert.Contains(tracks, track => track.Property == AnimationProperty.FONT_SIZE);
        Assert.DoesNotContain(tracks, track => track.Property == AnimationProperty.SCALE);
    }
}
