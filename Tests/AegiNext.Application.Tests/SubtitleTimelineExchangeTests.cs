using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.SubtitleFormats;

namespace AegiNext.Application.Tests;

public sealed class SubtitleTimelineExchangeTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(3000, 3000)]
    [InlineData(3100, 3000)]
    [InlineData(3000, 3200)]
    [InlineData(-100, -200)]
    public void KnownOriginsRoundTripExternalTimesWithoutChangingContentClock(long mediaMilliseconds, long playbackMilliseconds)
    {
        var binding = new ProjectMediaBinding(Guid.NewGuid(), 0, 1, new(mediaMilliseconds, 1000))
        {
            PlaybackOrigin = new(playbackMilliseconds, 1000)
        };
        var mapping = Assert.IsType<MediaTimelineMapping>(SubtitleTimelineExchange.GetMapping(binding));
        var source = new SubtitleLine { Start = new(1), End = new(2), Text = "cue" };

        var imported = Assert.Single(SubtitleTimelineExchange.ToProjectTime([source], mapping));

        Assert.Equal(source.Id, imported.Id);
        Assert.Equal(source.Start - new MediaTime(mediaMilliseconds - playbackMilliseconds, 1000), imported.Start);
        Assert.Equal(source.End - new MediaTime(mediaMilliseconds - playbackMilliseconds, 1000), imported.End);
        var restored = Assert.Single(SubtitleTextFormat.ParseSrt(SubtitleTextFormat.WriteSrt([imported], mapping.Origin)));
        Assert.Equal((source.Start, source.End, source.Text), (restored.Start, restored.End, restored.Text));
    }

    [Fact]
    public void UnknownBoundOriginIsDistinctFromAnUnboundIdentityTimeline()
    {
        Assert.Equal(default(MediaTimelineMapping), SubtitleTimelineExchange.GetMapping(null));
        Assert.Null(SubtitleTimelineExchange.GetMapping(new(Guid.NewGuid(), 0, null, new(3))));
    }

    [Fact]
    public void AssImportShiftsLineAndClipTogetherAndRetainsNegativeProjectTimesAndRelativeEffects()
    {
        var line = new SubtitleLine
        {
            Start = MediaTime.Zero, End = new(1, 2), Text = "a",
            Karaoke = [new(0, 1, MediaTime.Zero, new(1, 5), SceneColor.White)]
        };
        ImmutableArray<AnimationTrack> tracks = [new(AnimationProperty.OPACITY, [new(new(1, 20), 0.25), new(new(1, 5), 1)])];
        var clip = new SubtitleClipImport(line, null, tracks, new(1, 20));
        var imported = new AssImportResult([line], []) { Clips = [clip] };
        var original = ProjectEditingOperations.ImportSubtitleLines(new(), imported, "Before mapping");

        var shifted = SubtitleTimelineExchange.ToProjectTime(imported, new(new(1, 10)));
        var result = ProjectEditingOperations.ImportSubtitleLines(new(), shifted, "Imported");
        var subtitle = Assert.Single(result.Subtitles);
        var layer = Assert.Single(result.Layers);

        Assert.Equal(line.Id, subtitle.Id);
        Assert.Equal(new MediaTime(-1, 10), subtitle.Start);
        Assert.Equal(new MediaTime(2, 5), subtitle.End);
        Assert.Equal(shifted.Lines[0], shifted.Clips[0].Line);
        Assert.Equal((subtitle.Start, subtitle.End), (layer.Start, layer.End));
        Assert.Equal(line.Karaoke, subtitle.Karaoke);
        Assert.Equal(clip.ContentOffset, layer.AnimationOffset);
        Assert.Equal(tracks, layer.Tracks);
        Assert.Equal(original.Layers[0].Tracks, layer.Tracks);
        Assert.Equal(new MediaTime(3, 20), MediaTime.Zero - subtitle.Start + layer.AnimationOffset);
    }

    [Fact]
    public void AssAndSrtExportValidateExternalTimeAfterMappingRatherThanNegativeProjectTime()
    {
        var line = new SubtitleLine { Start = new(-1, 10), End = new(2, 5), Text = "a" };
        var document = Document(line);

        var ass = Assert.Single(AssSubtitleFormat.Parse(AssSubtitleFormat.Write(document, new(1, 10)).Text).Lines);
        var srt = Assert.Single(SubtitleTextFormat.ParseSrt(SubtitleTextFormat.WriteSrt([line], new(1, 10))));

        Assert.Equal((MediaTime.Zero, new MediaTime(1, 2)), (ass.Start, ass.End));
        Assert.Equal((ass.Start, ass.End), (srt.Start, srt.End));
        Assert.Equal(new MediaTime(-1, 10), document.Subtitles[0].Start);
    }

    [Fact]
    public void ExpandedAssMaskQuantizesOnExternalGridWithoutInventingNegativeDialogueTimes()
    {
        var line = new SubtitleLine { Start = new(-95, 1000), End = new(405, 1000), Text = "a" };
        var document = Document(line);
        var mask = new VectorClipMask
        {
            Contours = [new() { Nodes = [new() { Position = new(0, 0) }, new() { Position = new(100, 0) }, new() { Position = new(100, 100) }] }]
        };
        document = document with
        {
            Layers = [document.Layers[0] with
            {
                Mask = mask,
                Tracks = [new(AnimationProperty.MASK_POSITION,
                    [new(MediaTime.Zero, AnimationValue.FromVector(new(0, 0))), new(new(1, 2), AnimationValue.FromVector(new(20, 0)))])]
            }]
        };

        var result = AssSubtitleFormat.Write(document, new(96, 1000));
        var restored = AssSubtitleFormat.Parse(result.Text);

        Assert.Contains(result.Diagnostics, item => item.Code == "Ass.MaskAnimationExpanded");
        Assert.Equal(MediaTime.Zero, restored.Lines[0].Start);
        Assert.All(restored.Lines, cue => Assert.True(cue.Start >= MediaTime.Zero));
        Assert.Equal(new MediaTime(51, 100), restored.Lines[^1].End);
        Assert.Equal(new MediaTime(-95, 1000), document.Subtitles[0].Start);
    }

    [Fact]
    public void FinalNegativeExternalTimesAreRejectedWithoutClippingAndIdentifyTheCue()
    {
        var line = new SubtitleLine { Start = new(-1, 5), End = new(2, 5), Text = "a" };
        var ass = Assert.Throws<InvalidDataException>(() => AssSubtitleFormat.Write(Document(line), new(1, 10)));
        var srt = Assert.Throws<InvalidDataException>(() => SubtitleTextFormat.WriteSrt([line], new(1, 10)));

        Assert.Contains(line.Id.ToString(), ass.Message, StringComparison.Ordinal);
        Assert.Contains(line.Id.ToString(), srt.Message, StringComparison.Ordinal);
        Assert.Equal(new MediaTime(-1, 5), line.Start);
    }

    [Fact]
    public void LongFractionalFrameTimelineHasNoAccumulatingExchangeError()
    {
        var mapping = new MediaTimelineMapping(new(1001, 30000));
        for (var frame = 0L; frame <= 6L * 60 * 60 * 30; frame += 7919)
        {
            var time = new MediaTime(frame * 1001, 30000);
            Assert.Equal(time, mapping.ToMediaTime(mapping.ToProjectTime(time)));
        }
    }

    private static ProjectDocument Document(SubtitleLine line)
    {
        return new()
        {
            Subtitles = [line],
            Layers = [new() { Id = line.Id, Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End }]
        };
    }
}
