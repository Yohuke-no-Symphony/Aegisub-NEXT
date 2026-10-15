using System.Collections.Immutable;
using AegiNext.Core.Media;
using AegiNext.Desktop.Controllers;
using AegiNext.Desktop.Workspace;

namespace AegiNext.Desktop.Tests.Workspace;

public sealed class ExportAudioCompatibilityTests
{
    [Theory]
    [InlineData(null, "aac")]
    [InlineData(3, "aac")]
    [InlineData(1, "vorbis")]
    public void ExportAudioSelectionFollowsBindingOrFirstAudioRatherThanPreviewDefault(int? index, string expected)
    {
        var source = new MediaAssetInfo
        {
            Streams =
            [
                new() { Index = 0, CodecType = "video", Video = new() { Width = 64, Height = 48 } },
                new() { Index = 3, CodecType = "audio", CodecName = "aac" },
                new() { Index = 1, CodecType = "audio", CodecName = "vorbis", Disposition = new Dictionary<string, int> { ["default"] = 1 }.ToImmutableDictionary() }
            ]
        };

        Assert.Equal(expected, ExportAudioCompatibility.ReadAudioCodec(source, index));
        var preview = VideoPreviewProbe.Read(source);
        Assert.Equal(1, preview.AudioStreamIndex);
        Assert.Equal("vorbis", preview.AudioCodecName);
    }

    [Fact]
    public void NoAudioRemainsAbsentRatherThanRequestingTranscoding()
    {
        Assert.Null(ExportAudioCompatibility.ReadAudioCodec(new(), null));
    }

    [Fact]
    public void SelectedNonAudioStreamAndUnknownCodecCannotBeTreatedAsCompatibleCopy()
    {
        var source = new MediaAssetInfo
        {
            Streams = [new() { Index = 0, CodecType = "video" }, new() { Index = 1, CodecType = "audio" }]
        };

        Assert.Throws<InvalidDataException>(() => ExportAudioCompatibility.ReadAudioCodec(source, 0));
        Assert.Throws<InvalidDataException>(() => ExportAudioCompatibility.ReadAudioCodec(source, 1));
    }
}
