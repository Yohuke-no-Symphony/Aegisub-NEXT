using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Export.Tests;

public sealed class AudioExportRequestTests
{
    [Theory]
    [InlineData(AudioFileFormat.Copy, ".mka")]
    [InlineData(AudioFileFormat.Wav, ".wav")]
    [InlineData(AudioFileFormat.Flac, ".flac")]
    [InlineData(AudioFileFormat.Mp3, ".mp3")]
    [InlineData(AudioFileFormat.Aac, ".m4a")]
    [InlineData(AudioFileFormat.Opus, ".opus")]
    public void AudioOnlyProjectsWithoutSubtitlesCanExportEachFormat(AudioFileFormat format, string extension)
    {
        using var directory = new ExportTestDirectory();
        var request = CreateRequest(directory.Path, format);

        AudioExportRequestValidator.Validate(request);

        Assert.Equal(extension, Path.GetExtension(request.OutputPath));
        Assert.Null(request.Project.Media!.VideoStreamIndex);
        Assert.Empty(request.Project.Subtitles);
    }

    [Fact]
    public void MissingMediaAndMismatchedOutputExtensionsAreRejected()
    {
        using var directory = new ExportTestDirectory();
        var request = CreateRequest(directory.Path, AudioFileFormat.Wav);

        Assert.Throws<ArgumentException>(() => AudioExportRequestValidator.Validate(request with
        {
            Project = request.Project with { Media = null }
        }));
        Assert.Throws<ArgumentException>(() => AudioExportRequestValidator.Validate(request with
        {
            OutputPath = Path.Combine(directory.Path, "output.mp3")
        }));
    }

    [Theory]
    [InlineData(AudioFileFormat.Mp3, 321000)]
    [InlineData(AudioFileFormat.Aac, 0)]
    [InlineData(AudioFileFormat.Opus, 513000)]
    public void InvalidActiveBitratesAreRejected(AudioFileFormat format, int bitrate)
    {
        using var directory = new ExportTestDirectory();
        var request = CreateRequest(directory.Path, format) with { Bitrate = bitrate };

        Assert.Throws<ArgumentOutOfRangeException>(() => AudioExportRequestValidator.Validate(request));
    }

    [Theory]
    [InlineData(AudioFileFormat.Copy)]
    [InlineData(AudioFileFormat.Wav)]
    [InlineData(AudioFileFormat.Flac)]
    public void InactiveBitrateDoesNotBlockCopyOrUncompressedFormats(AudioFileFormat format)
    {
        using var directory = new ExportTestDirectory();
        AudioExportRequestValidator.Validate(CreateRequest(directory.Path, format) with { Bitrate = 0 });
    }

    internal static AudioExportRequest CreateRequest(string directory, AudioFileFormat format)
    {
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, string.Empty,
            ExternalPath: Path.Combine(directory, "source.mka"));
        var project = new ProjectDocument
        {
            Assets = [asset],
            Media = new(asset.Id, null, 0, MediaTime.Zero)
        };
        return new(project, directory, Path.Combine(directory, "output" + AudioExportCapabilities.GetExtension(format)))
        {
            Format = format
        };
    }
}
