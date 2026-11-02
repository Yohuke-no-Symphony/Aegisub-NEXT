using System.Collections.Immutable;
using AegiNext.Core.Media;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controllers;
using AegiNext.Desktop.Workspace;

namespace AegiNext.Desktop.Tests.Controllers;

public sealed class AudioOnlyPreviewProbeTests
{
    [Fact]
    public void AudioOnlyAssetUsesTheAudioStreamTimingWithoutVideoStream()
    {
        var media = VideoPreviewProbe.Read(new()
        {
            Streams =
            [
                new() { Index = 0, CodecType = "audio", CodecName = "flac", Timing = Timing(250, 180_000) }
            ]
        });

        Assert.True(media.IsAudioOnly);
        Assert.Null(media.VideoStreamIndex);
        Assert.Equal(0, media.AudioStreamIndex);
        Assert.Equal(new MediaTime(250, 1000), media.Start);
        Assert.Equal(new MediaTime(180), media.Duration);
        Assert.Null(media.VideoWidth);
        Assert.Null(media.FrameRate);
        Assert.Equal("flac", media.AudioCodecName);
    }

    [Fact]
    public void CoverArtIsIgnoredSoTaggedMusicFilesOpenAsAudioOnly()
    {
        var media = VideoPreviewProbe.Read(new()
        {
            Streams =
            [
                new() { Index = 0, CodecType = "audio", Timing = Timing(0, 1000) },
                new()
                {
                    Index = 1, CodecType = "video", Video = new() { Width = 500, Height = 500 },
                    Disposition = new Dictionary<string, int> { ["attached_pic"] = 1 }.ToImmutableDictionary()
                }
            ]
        });

        Assert.True(media.IsAudioOnly);
        Assert.Equal(0, media.AudioStreamIndex);
    }

    [Fact]
    public void FilesWithoutVideoOrAudioStillFail()
    {
        Assert.Throws<InvalidDataException>(() => VideoPreviewProbe.Read(new()
        {
            Streams = [new() { Index = 0, CodecType = "subtitle" }]
        }));
    }

    [Theory]
    [InlineData(".flac")]
    [InlineData(".MP3")]
    [InlineData(".wav")]
    [InlineData(".m4a")]
    [InlineData(".opus")]
    public void AudioFileTypesAcceptCommonAudioExtensions(string extension)
    {
        Assert.True(AudioFileTypes.SupportsPath("song" + extension));
        Assert.Contains("*" + extension.ToLowerInvariant(), AudioFileTypes.Patterns);
    }

    [Fact]
    public void AudioAndVideoFileTypesDoNotOverlapExceptForNone()
    {
        Assert.Empty(AudioFileTypes.Patterns.Intersect(VideoFileTypes.Patterns, StringComparer.OrdinalIgnoreCase));
    }

    private static MediaStreamTiming Timing(long start, long duration)
    {
        return new() { StartPts = start, DurationTicks = duration, TimeBase = new(1, 1000) };
    }
}
