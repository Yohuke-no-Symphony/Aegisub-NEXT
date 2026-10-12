using System.Collections.Immutable;
using System.Text.Json.Nodes;
using AegiNext.Core.Media;
using AegiNext.Core.Timing;
using AegiNext.Media.Probing;

namespace AegiNext.Media.Tests.Probing;

public sealed class VideoTimingIndexTests
{
    private const string VIDEO_JSON = """
        {"streams":[{"index":2,"codec_type":"video","time_base":"1/1000"}],
         "frames":[
          {"stream_index":2,"key_frame":1,"pts":5000,"best_effort_timestamp":5000},
          {"stream_index":2,"key_frame":0,"pts":5040,"best_effort_timestamp":5040},
          {"stream_index":2,"key_frame":1,"pts":5160,"best_effort_timestamp":5160},
          {"stream_index":2,"key_frame":0,"pts":5200,"best_effort_timestamp":5200}]}
        """;

    [Theory]
    [InlineData(-10, false, 0)]
    [InlineData(0, false, 0)]
    [InlineData(40, false, 1)]
    [InlineData(120, false, 1)]
    [InlineData(160, false, 2)]
    [InlineData(400, false, 3)]
    [InlineData(0, true, 0)]
    [InlineData(40, true, 0)]
    [InlineData(160, true, 1)]
    [InlineData(200, true, 2)]
    [InlineData(201, true, 3)]
    public void UsesActualVariableFrameTimesAndExclusiveEnd(int milliseconds, bool end, int expected)
    {
        var index = new VideoTimingIndex([new(0), new(1, 25), new(4, 25), new(1, 5)], [0, 2, 3]);

        Assert.Equal(expected, index.FrameAtTime(new(milliseconds, 1000), end));
    }

    [Fact]
    public void NearestKeyframeUsesFrameDistanceAndPrefersEarlierTie()
    {
        var index = new VideoTimingIndex([new(0), new(1, 1000), new(2, 1000), new(10), new(11)], [0, 4]);

        Assert.Equal(0, index.NearestKeyframe(2));
        Assert.Equal(4, index.NearestKeyframe(3));
        Assert.Equal(new MediaTime(11), index.KeyframeBoundary(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => index.NearestKeyframe(5));
        Assert.Throws<ArgumentOutOfRangeException>(() => index.KeyframeBoundary(-1));
    }

    [Fact]
    public void RejectsEmptyUninitializedAndNonIncreasingFrames()
    {
        Assert.Throws<ArgumentException>(() => new VideoTimingIndex([], []));
        Assert.Throws<ArgumentException>(() => new VideoTimingIndex(default, []));
        Assert.Throws<ArgumentException>(() => new VideoTimingIndex([new(0)], default));
        Assert.Throws<ArgumentException>(() => new VideoTimingIndex([new(0), new(0)], [0]));
        Assert.Throws<ArgumentException>(() => new VideoTimingIndex([new(1), new(0)], [0]));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new VideoTimingIndex(ImmutableArray.Create(new MediaTime[VideoTimingIndex.MaximumFrameCount + 1]), []));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 2)]
    [InlineData(1, 0)]
    [InlineData(0, 0)]
    public void RejectsInvalidKeyframeIndices(int first, int second)
    {
        Assert.Throws<ArgumentException>(() => new VideoTimingIndex([new(0), new(1)], [first, second]));
    }

    [Fact]
    public void AllowsNegativeNormalizedPtsAndEmptyKeyframesWithoutInventingFrames()
    {
        var index = new VideoTimingIndex([new(-1, 25), new(0)], []);

        Assert.Equal(0, index.FrameAtTime(new(-1, 1000)));
        Assert.Throws<InvalidOperationException>(() => index.NearestKeyframe(0));
    }

    [Fact]
    public void ReadsExactPtsSubtractsOriginAndIncludesLastFrameCandidate()
    {
        var index = FfprobeVideoTimingJsonReader.Read(VIDEO_JSON, 2, new(5));

        Assert.Equal(new MediaTime[] { new(0), new(1, 25), new(4, 25), new(1, 5) }, index.FrameTimes);
        Assert.Equal<int>([0, 2, 3], index.Keyframes);
    }

    [Fact]
    public void PreservesSubMillisecondTimebaseWithoutFloatingPoint()
    {
        var json = JsonNode.Parse(VIDEO_JSON)!;
        json["streams"]![0]!["time_base"] = "1001/24000";
        var index = FfprobeVideoTimingJsonReader.Read(json.ToJsonString(), 2, new(5005, 24));

        Assert.Equal(MediaTime.Zero, index.FrameTimes[0]);
        Assert.Equal(new MediaTime(1001, 600), index.FrameTimes[1]);
    }

    [Theory]
    [InlineData("pts", null)]
    [InlineData("pts", "N/A")]
    [InlineData("pts", "-9223372036854775808")]
    [InlineData("pts", "1.5")]
    [InlineData("pts", "9223372036854775808")]
    [InlineData("best_effort_timestamp", "5001")]
    [InlineData("stream_index", "3")]
    [InlineData("key_frame", "2")]
    public void RejectsMissingInvalidAndInconsistentFrameFacts(string field, string? value)
    {
        var json = JsonNode.Parse(VIDEO_JSON)!;
        json["frames"]![0]![field] = value;

        Assert.Throws<InvalidDataException>(() => FfprobeVideoTimingJsonReader.Read(json.ToJsonString(), 2, new(5)));
    }

    [Theory]
    [InlineData(5000)]
    [InlineData(4999)]
    public void RejectsDuplicateOrBackwardDisplayTimestamps(long secondPts)
    {
        var json = JsonNode.Parse(VIDEO_JSON)!;
        json["frames"]![1]!["pts"] = secondPts;
        json["frames"]![1]!["best_effort_timestamp"] = secondPts;

        var error = Assert.Throws<InvalidDataException>(() => FfprobeVideoTimingJsonReader.Read(json.ToJsonString(), 2, new(5)));

        Assert.Contains("strictly increasing", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{}")]
    [InlineData("{\"streams\":[],\"frames\":[]}")]
    [InlineData("{\"streams\":[{\"index\":2,\"codec_type\":\"audio\",\"time_base\":\"1/1000\"}],\"frames\":[]}")]
    [InlineData("{\"streams\":[{\"index\":2,\"codec_type\":\"video\",\"time_base\":\"0/0\"}],\"frames\":[]}")]
    [InlineData("{\"streams\":[{\"index\":2,\"codec_type\":\"video\",\"time_base\":\"1/-1000\"}],\"frames\":[]}")]
    public void RejectsIncompleteOrMalformedScans(string json)
    {
        Assert.Throws<InvalidDataException>(() => FfprobeVideoTimingJsonReader.Read(json, 2, default));
    }

    [Fact]
    public void CancelledParsingPreservesCallerToken()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var error = Assert.ThrowsAny<OperationCanceledException>(() =>
            FfprobeVideoTimingJsonReader.Read(VIDEO_JSON, 2, new(5), cancellation.Token));

        Assert.Equal(cancellation.Token, error.CancellationToken);
    }

    [Fact]
    public void RejectsDuplicateJsonPropertiesAndExactTimeOverflow()
    {
        var duplicate = VIDEO_JSON.Replace("\"pts\":5000", "\"pts\":5000,\"pts\":5000", StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => FfprobeVideoTimingJsonReader.Read(duplicate, 2, new(5)));

        var json = JsonNode.Parse(VIDEO_JSON)!;
        json["streams"]![0]!["time_base"] = "9223372036854775807/1";
        Assert.Throws<InvalidDataException>(() => FfprobeVideoTimingJsonReader.Read(json.ToJsonString(), 2, new(5)));
    }
}
